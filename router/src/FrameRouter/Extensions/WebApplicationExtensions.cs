using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FrameRouter.Mzk;
using FrameRouter.Screens;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace FrameRouter.Extensions;

internal static class WebApplicationExtensions
{
    private const int DefaultDepartureLimit = 8;
    private const int MaxConfigBytes = 64 * 1024;
    private const int MinStopQueryLength = 2;
    private const int MaxStopQueryLength = 40;
    private const int MaxOverrideMinutes = 24 * 60;

    public static WebApplication MapFrameRouterEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", () => TypedResults.Text("ok"));

        app.MapGet("/api/frames/{frameId}/screen", (string frameId, ScreenState screenState) =>
            ScreenConfigParser.IsValidFrameId(frameId)
                ? Results.Ok(screenState.GetSnapshot(frameId))
                : Results.BadRequest("Invalid frame id."));

        // ApplicationStopping ends open streams at once; otherwise each one holds shutdown for 30 s and docker stop kills the app.
        app.MapGet("/api/frames/{frameId}/stream", (string frameId, ScreenState screenState, IHostApplicationLifetime lifetime, CancellationToken cancellationToken) =>
            ScreenConfigParser.IsValidFrameId(frameId)
                ? Results.ServerSentEvents(screenState.StreamAsync(frameId, cancellationToken, lifetime.ApplicationStopping))
                : Results.BadRequest("Invalid frame id."));

        app.MapGet("/api/departures", GetDeparturesAsync);
        app.MapGet("/api/stops", FindStopsAsync);
        app.MapGet("/api/config", (ConfigStore configStore) => Results.Ok(configStore.Current));

        // Same token check on frame.vicio.ovh and frame.home.vicio.ovh: neither host puts its own auth in front of the router.
        var options = app.Services.GetRequiredService<IOptions<FrameRouterOptions>>().Value;
        app.MapPost("/api/config", ApplyConfigAsync).RequireBearer(options.ConfigToken, "FrameRouter__ConfigToken");

        // The control page and Home Assistant set temporary screens here.
        var control = app.MapGroup("/api/control").RequireBearer(options.ControlToken, "FrameRouter__ControlToken");
        control.MapGet("/frames", GetControlState);
        control.MapPut("/frames/{frameId}/override", SetOverride);
        control.MapDelete("/frames/{frameId}/override", ClearOverride);

        return app;
    }

    internal sealed record ControlFrame
    {
        public required string Id { get; init; }

        // Null when screens.json gives none; the control page shows the id then.
        public string? Label { get; init; }

        public required ScreenSnapshot Snapshot { get; init; }
    }

    internal sealed record ControlState
    {
        public required IReadOnlyList<string> Screens { get; init; }

        public required IReadOnlyList<ControlFrame> Frames { get; init; }
    }

    internal sealed record OverrideRequest
    {
        public string? Screen { get; init; }

        public int? Minutes { get; init; }
    }

    private static ControlState GetControlState(ConfigStore configStore, ScreenState screenState)
    {
        var config = configStore.Current;
        return new ControlState
        {
            Screens = config.Screens.Keys.ToList(),
            Frames = config.Frames.Select(frame => new ControlFrame { Id = frame.Key, Label = frame.Value.Label, Snapshot = screenState.GetSnapshot(frame.Key) }).ToList(),
        };
    }

    private static IResult SetOverride(string frameId, OverrideRequest body, ConfigStore configStore, ScreenState screenState)
    {
        var config = configStore.Current;
        if (!config.Frames.ContainsKey(frameId))
            return FrameNotFound(frameId);

        if (body.Screen is null || !config.Screens.ContainsKey(body.Screen))
            return Results.BadRequest($"Pass \"screen\" with one of: {string.Join(", ", config.Screens.Keys)}.");

        if (body.Minutes is not (>= 1 and <= MaxOverrideMinutes))
            return Results.BadRequest($"Pass \"minutes\" from 1 to {MaxOverrideMinutes}.");

        screenState.SetOverride(frameId, body.Screen, TimeSpan.FromMinutes(body.Minutes.Value));
        return Results.Ok(screenState.GetSnapshot(frameId));
    }

    private static IResult ClearOverride(string frameId, ConfigStore configStore, ScreenState screenState)
    {
        if (!configStore.Current.Frames.ContainsKey(frameId))
            return FrameNotFound(frameId);

        screenState.ClearOverride(frameId);
        return Results.Ok(screenState.GetSnapshot(frameId));
    }

    private static IResult FrameNotFound(string frameId) => Results.NotFound($"Frame \"{frameId}\" is not in screens.json.");

    private static async Task<IResult> GetDeparturesAsync(string? stops, int? limit, DeparturesService departuresService, CancellationToken cancellationToken)
    {
        var stopIds = ParseStopIds(stops);
        if (stopIds is null or { Count: 0 } || stopIds.Count > DeparturesService.MaxStopsPerRequest)
            return Results.BadRequest($"Pass 1 to {DeparturesService.MaxStopsPerRequest} stop ids, like ?stops=11,28.");

        var board = await departuresService.GetBoardAsync(stopIds, Math.Clamp(limit ?? DefaultDepartureLimit, 1, ScreenConfigParser.MaxDepartureLimit), cancellationToken);
        return board.Stops.Count == 0
            ? Results.Problem("MZK did not answer for any of the stops.", statusCode: StatusCodes.Status502BadGateway)
            : Results.Ok(board);
    }

    private static async Task<IResult> FindStopsAsync(string? q, DeparturesService departuresService, CancellationToken cancellationToken)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length is < MinStopQueryLength or > MaxStopQueryLength)
            return Results.BadRequest($"Type {MinStopQueryLength} to {MaxStopQueryLength} letters of the stop name.");

        var stops = await departuresService.FindStopsAsync(query, cancellationToken);
        return stops is null
            ? Results.Problem("MZK stop search failed.", statusCode: StatusCodes.Status502BadGateway)
            : Results.Ok(stops);
    }

    private static async Task<IResult> ApplyConfigAsync(HttpRequest request, ConfigStore configStore, ScreenState screenState, CancellationToken cancellationToken)
    {
        // Kestrel answers 413 itself when the body passes the limit, chunked or not.
        request.HttpContext.Features.GetRequiredFeature<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = MaxConfigBytes;

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var errors = await configStore.ApplyAsync(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        if (errors.Count > 0)
            return Results.BadRequest(new { errors });

        screenState.Refresh();
        return Results.Ok(configStore.Current.Frames.Keys.Select(screenState.GetSnapshot));
    }

    private static TBuilder RequireBearer<TBuilder>(this TBuilder builder, string token, string settingName)
        where TBuilder : IEndpointConventionBuilder
    {
        var expected = Encoding.UTF8.GetBytes(token);
        return builder.AddEndpointFilter((context, next) =>
        {
            if (expected.Length == 0)
                return ValueTask.FromResult<object?>(Results.Problem($"This endpoint is off: {settingName} is not set.", statusCode: StatusCodes.Status403Forbidden));

            const string scheme = "Bearer ";
            var header = context.HttpContext.Request.Headers.Authorization.ToString();
            var authorized = header.StartsWith(scheme, StringComparison.Ordinal)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[scheme.Length..]), expected);
            return authorized ? next(context) : ValueTask.FromResult<object?>(Results.Unauthorized());
        });
    }

    private static List<int>? ParseStopIds(string? stops)
    {
        List<int> ids = [];
        foreach (var part in (stops ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                return null;
            ids.Add(id);
        }

        return ids.Distinct().ToList();
    }
}
