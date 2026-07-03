using PcgUtil.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // Allow large PCG uploads to stream over the SignalR circuit.
        options.MaximumReceiveMessageSize = 64L * 1024 * 1024;
    });

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
