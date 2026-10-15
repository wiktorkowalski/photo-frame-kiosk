using System.Text.Json.Serialization;
using FrameRouter.Mzk;
using FrameRouter.Screens;

namespace FrameRouter.Extensions;

internal static class ServiceCollectionExtensions
{
    private static readonly TimeSpan MzkTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddFrameRouter(this IServiceCollection services)
    {
        services.AddOptions<FrameRouterOptions>()
            .BindConfiguration(FrameRouterOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out _), "FrameRouter:TimeZone is not a known time zone.")
            .ValidateOnStart();

        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ConfigStore>();
        services.AddSingleton<ScreenState>();
        services.AddHostedService<ScreenTicker>();

        services.AddHttpClient<MzkClient>(client =>
        {
            client.BaseAddress = MzkClient.BaseAddress;
            client.Timeout = MzkTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("frame-router (+https://github.com/wiktorkowalski/photo-frame-kiosk)");
        });
        services.AddSingleton<DeparturesService>();

        return services;
    }
}
