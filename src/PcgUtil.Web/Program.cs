using PcgUtil.Web;
using PcgUtil.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // A tab backgrounded on a phone routinely drops its SignalR connection; the default
        // 3-minute disconnected-circuit retention then discards the whole in-memory session
        // (the ~47 MB uploaded PCG and any unsaved edits). Keep circuits reconnectable for
        // 30 minutes instead.
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30);
        // 32-bit app pool (deploy-ftp.ps1 publishes win-x86; ~2 GB usable address space):
        // each retained circuit can pin a loaded PCG (~47 MB) plus a Copy-tab source file
        // (~47 MB more), so 4 retained circuits ≈ ≤ 376 MB worst case — leaving headroom
        // for live circuits and the 150 MB UploadCache.
        options.DisconnectedCircuitMaxRetained = 4;
    })
    .AddHubOptions(options =>
    {
        // Allow large PCG uploads to stream over the SignalR circuit.
        options.MaximumReceiveMessageSize = 64L * 1024 * 1024;
    });

// Session-restore cache: outlives any single circuit, so an evicted circuit or a page
// reload can offer the upload back instead of losing it (memory-only, never disk).
builder.Services.AddSingleton<UploadCache>();

// Behind IIS in-process the middleware can't infer the HTTPS port; the public site uses 443.
builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    // Production-only: the local http launch profile has no TLS endpoint to redirect to.
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
