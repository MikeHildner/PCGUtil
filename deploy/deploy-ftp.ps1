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
    dotnet publish (Join-Path $repoRoot 'src/PcgUtil.Web') -c Release -r win-x86 --self-contained true -p:PublishReadyToRun=true -o $outDir --nologo
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

function Invoke-FtpUpload([string]$LocalFile, [string]$RemoteRelative) {
    $url = "$ftpBase/$RemoteRelative" -replace '\\', '/'
    # The shared host intermittently answers 550 after a complete transfer (storage/scanner
    # hiccups); a short retry turns those into non-events. Persistent failures still throw.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        & curl.exe -sS --ssl-reqd --ssl-no-revoke --ftp-create-dirs -u $curlAuth -T $LocalFile $url
        if ($LASTEXITCODE -eq 0) { return }
        Write-Host "  retry $attempt/2 for $RemoteRelative (curl $LASTEXITCODE)"
        Start-Sleep -Seconds (3 * $attempt)
    }
    throw "Upload failed after retries: $RemoteRelative"
}

function Invoke-FtpDelete([string]$RemoteRelative) {
    # DELE needs the path relative to the FTP root; -Q runs it after connecting.
    & curl.exe -sS --ssl-reqd --ssl-no-revoke -u $curlAuth "ftp://$($secrets.host)/" -Q "DELE /$remotePath/$RemoteRelative" 2>$null
    # non-fatal: file may not exist on a first deploy
}

# ----- 3. Stop the app (app_offline.htm), upload everything, restart -----
$appOffline = Join-Path $env:TEMP 'app_offline.htm'
Set-Content $appOffline '<html><body><h1>PCG Util is being updated&hellip;</h1><p>Back in a minute.</p></body></html>' -Encoding utf8
Write-Host 'Taking the app offline ...'
Invoke-FtpUpload $appOffline 'app_offline.htm'
Start-Sleep -Seconds 5  # give ANCM a moment to shut down and release file locks

$files = Get-ChildItem $outDir -Recurse -File
$total = $files.Count
Write-Host "Uploading $total files ($([math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB)) MB) ..."
$i = 0
foreach ($file in $files) {
    $i++
    $rel = $file.FullName.Substring($outDir.Length + 1) -replace '\\', '/'
    if ($i % 25 -eq 0 -or $i -eq $total) { Write-Host ("  {0}/{1}  {2}" -f $i, $total, $rel) }
    Invoke-FtpUpload $file.FullName $rel
}

Write-Host 'Bringing the app back online ...'
Invoke-FtpDelete 'app_offline.htm'
Remove-Item $appOffline -Force

Write-Host 'Done. Browse: https://hildner.org/pcgutil/'
