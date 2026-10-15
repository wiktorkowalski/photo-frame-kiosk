using FrameRouter.Extensions;
using FrameRouter.Screens;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = WebApplicationExtensions.MaxRequestBytes);
builder.Services.AddFrameRouter();

var app = builder.Build();
app.Services.GetRequiredService<ConfigStore>().Load();
app.Services.GetRequiredService<OverrideStore>().Load();

app.UseDefaultFiles();
// The frame page must pick up a new frame.js after a deploy; a revalidation per load is cheap on the LAN.
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache" });
app.MapFrameRouterEndpoints();

app.Run();
