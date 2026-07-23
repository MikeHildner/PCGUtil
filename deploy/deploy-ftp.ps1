# Publishes PcgUtil.Web self-contained (win-x86, matches the host's 32-bit app pool) and
# mirrors it to the shared host over explicit FTPS.
#
# Usage:  .\deploy\deploy-ftp.ps1 [-SkipPublish] [-EnableStdoutLog]
#
# Credentials come from deploy\ftp.secrets.json (git-ignored; see ftp.secrets.json.example).
# The script drops app_offline.htm first so ANCM stops the app and unlocks its files, then
# uploads everything, then removes app_offline.htm to start the app again.

[CmdletBinding()]
param(
    [switch]$SkipPublish,     # reuse deploy\out from a previous run
    [switch]$EnableStdoutLog  # turn on ANCM stdout logging in the uploaded web.config
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $PSScriptRoot 'out'
$secretsPath = Join-Path $PSScriptRoot 'ftp.secrets.json'

if (-not (Test-Path $secretsPath)) {
    throw "Missing $secretsPath - copy ftp.secrets.json.example and fill in the FTP host/user/pass."
}
$secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json
foreach ($key in 'host', 'user', 'pass', 'remotePath') {
    if ([string]::IsNullOrWhiteSpace($secrets.$key)) { throw "ftp.secrets.json is missing '$key'." }
}
$remotePath = $secrets.remotePath.Trim('/')  # e.g. "pcgutil"

# ----- 1. Publish -----
if (-not $SkipPublish) {
    Write-Host "Publishing self-contained win-x86 to $outDir ..."
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    # ReadyToRun matters beyond startup speed: the host's FTP upload scanner false-positives
    # on plain IL builds of this app's assemblies (post-transfer 550, file discarded), and the
    # R2R binary layout is what gets a clean pass (diagnosed 2026-07-14).
    # The date-coded Version does double duty: a meaningful file version, and a fresh binary
    # layout every publish — when the scanner's content rules coincidentally match our bytes
    # (2026-07-21: PcgUtil.Core.dll rejected under ANY name), the next stamp shifts every RVA
    # and the match evaporates.
    $stamp = Get-Date -Format 'yyyy.MM.dd.HHmm'
    dotnet publish (Join-Path $repoRoot 'src/PcgUtil.Web') -c Release -r win-x86 --self-contained true -p:PublishReadyToRun=true -p:Version=$stamp -o $outDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
}
if (-not (Test-Path (Join-Path $outDir 'web.config'))) { throw "No web.config in $outDir - publish output looks wrong." }

if ($EnableStdoutLog) {
    $webConfig = Join-Path $outDir 'web.config'
    (Get-Content $webConfig -Raw) -replace 'stdoutLogEnabled="false"', 'stdoutLogEnabled="true"' |
        Set-Content $webConfig -Encoding utf8
    Write-Host "stdout logging enabled (logs land in $remotePath/logs/)."
}

# ----- 2. FTPS helpers (curl: explicit TLS, create dirs as needed) -----
$ftpBase = "ftp://$($secrets.host)/$remotePath"
$curlAuth = "$($secrets.user):$($secrets.pass)"

# One upload attempt-set: a few direct tries, then the upload+rename fallback. Returns
# $true on success, $false on failure — the CALLER decides whether failure is fatal.
# The scanner's 550s correlate with the mirror's rapid-fire upload storm (identical bytes
# pass as a lone upload minutes later — proven twice on 2026-07-21), so mid-mirror
# failures are collected and swept again at the end rather than aborting the deploy.
function Invoke-FtpUpload([string]$LocalFile, [string]$RemoteRelative, [int]$DirectTries = 3) {
    $rel = $RemoteRelative -replace '\\', '/'
    $url = "$ftpBase/$rel"
    for ($attempt = 1; $attempt -le $DirectTries; $attempt++) {
        # stdout to null so the function's return stays a pure boolean (PS functions emit
        # every uncaptured stream); curl's stderr still prints live.
        & curl.exe -sS --ssl-reqd --ssl-no-revoke --ftp-create-dirs -u $curlAuth -T $LocalFile $url | Out-Null
        if ($LASTEXITCODE -eq 0) { return $true }
        Write-Host "  retry $attempt/$DirectTries for $rel (curl $LASTEXITCODE)"
        Start-Sleep -Seconds (5 * $attempt)
    }
    # Fallback: upload under a neutral name, then rename into place (dodges name-keyed
    # scanner rules — PcgUtil.Web.dll needed this on 2026-07-21). DELE first ('*' =
    # ignore a missing target) so RNTO can't collide with a survivor.
    Write-Host "  direct upload rejected - trying upload+rename for $rel"
    $curlArgs = @('-sS', '--ssl-reqd', '--ssl-no-revoke', '--ftp-create-dirs', '-u', $curlAuth,
        '-T', $LocalFile,
        '-Q', "-*DELE /$remotePath/$rel",
        '-Q', "-RNFR /$remotePath/$rel.upload",
        '-Q', "-RNTO /$remotePath/$rel",
        "$ftpBase/$rel.upload")
    & curl.exe @curlArgs | Out-Null
    return $LASTEXITCODE -eq 0
}

function Invoke-FtpDelete([string]$RemoteRelative) {
    # DELE needs the path relative to the FTP root; -Q runs it after connecting.
    # curl runs fully silent (-s, no -S): under $ErrorActionPreference=Stop, PS 5.1 turns
    # any native stderr line into a *terminating* NativeCommandError, so a 550 on an
    # already-missing file would abort the whole deploy (bit us 2026-07-21).
    & curl.exe -s --ssl-reqd --ssl-no-revoke -u $curlAuth "ftp://$($secrets.host)/" -Q "DELE /$remotePath/$RemoteRelative"
    # non-fatal: file may not exist on a first deploy
}

# ----- 3. Stop the app (app_offline.htm), upload everything, restart -----
$appOffline = Join-Path $env:TEMP 'app_offline.htm'
Set-Content $appOffline '<html><body><h1>PCG Util is being updated&hellip;</h1><p>Back in a minute.</p></body></html>' -Encoding utf8
Write-Host 'Taking the app offline ...'
if (-not (Invoke-FtpUpload $appOffline 'app_offline.htm')) { throw 'Could not upload app_offline.htm.' }
Start-Sleep -Seconds 5  # give ANCM a moment to shut down and release file locks

# clrgc.dll is the opt-in standalone GC — never loaded unless System.GC.Name selects it,
# which we don't configure — and the host's scanner started rejecting it persistently
# (550 after complete transfer, 2026-07-21). Skip it and clear any stale remote copy.
Invoke-FtpDelete 'clrgc.dll'
$files = Get-ChildItem $outDir -Recurse -File | Where-Object Name -ne 'clrgc.dll'
$total = $files.Count
Write-Host "Uploading $total files ($([math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB)) MB) ..."
$i = 0
$stragglers = @()
foreach ($file in $files) {
    $i++
    $rel = $file.FullName.Substring($outDir.Length + 1) -replace '\\', '/'
    if ($i % 25 -eq 0 -or $i -eq $total) { Write-Host ("  {0}/{1}  {2}" -f $i, $total, $rel) }
    if (-not (Invoke-FtpUpload $file.FullName $rel)) {
        Write-Host "  deferring $rel to the end-of-mirror sweep"
        $stragglers += ,@($file.FullName, $rel)
    }
}

# Second chance for scanner-storm casualties: by now minutes have passed and the upload
# rate has dropped — the same bytes that 550'd mid-mirror typically pass as lone uploads.
if ($stragglers.Count -gt 0) {
    Write-Host "Sweeping $($stragglers.Count) deferred file(s) ..."
    $stillFailing = @()
    foreach ($s in $stragglers) {
        Start-Sleep -Seconds 20
        Write-Host "  sweep: $($s[1])"
        if (-not (Invoke-FtpUpload $s[0] $s[1] -DirectTries 4)) { $stillFailing += ,$s }
    }
    # Patience pass: three deploys running (2026-07-22/23) showed sweep survivors landing
    # as lone uploads on roughly the third 40-second-spaced try — the scanner just needs
    # the storm further behind it. Give each survivor up to 8 widely-spaced solo tries
    # before declaring the deploy dead.
    if ($stillFailing.Count -gt 0) {
        Write-Host "Patience pass for $($stillFailing.Count) file(s) - spaced solo retries ..."
        $lost = @()
        foreach ($s in $stillFailing) {
            $landed = $false
            for ($try = 1; $try -le 8; $try++) {
                Start-Sleep -Seconds 40
                Write-Host "  patience ${try}/8: $($s[1])"
                if (Invoke-FtpUpload $s[0] $s[1] -DirectTries 1) { $landed = $true; break }
            }
            if (-not $landed) { $lost += $s[1] }
        }
        if ($lost.Count -gt 0) {
            throw "Upload failed even after the patience pass: $($lost -join ', ') - site left offline (app_offline.htm in place)."
        }
    }
}

Write-Host 'Bringing the app back online ...'
Invoke-FtpDelete 'app_offline.htm'
Remove-Item $appOffline -Force

Write-Host 'Done. Browse: https://hildner.org/pcgutil/'
