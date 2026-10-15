using System.Globalization;
using System.Text;
using FrameRouter.Mzk;
using FrameRouter.Screens;
using Microsoft.Extensions.Options;

namespace FrameRouter.Extensions;

internal static class WebApplicationExtensions
{
    // Kestrel answers 413 above this, chunked or not, so no endpoint reads a large unauthenticated body. The config is the largest one.
    public const int MaxRequestBytes = 64 * 1024;

    private const int DefaultDepartureLimit = 8;
    private const int MinStopQueryLength = 2;
    private const int MaxStopQueryLength = 40;
    private const int MaxOverrideMinutes = 24 * 60;
    private const int DefaultOverrideMinutes = 30;

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

        var options = app.Services.GetRequiredService<IOptions<FrameRouterOptions>>().Value;
        app.MapPost("/api/config", ApplyConfigAsync).RequireBearer(options.ConfigToken, "FrameRouter__ConfigToken");

        var control = app.MapGroup("/api/control").RequireBearer(options.ControlToken, "FrameRouter__ControlToken");
        control.MapGet("/frames", GetControlState);
        control.MapPut("/frames/{frameId}/override", SetOverrideAsync);
        control.MapDelete("/frames/{frameId}/override", ClearOverrideAsync);

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
            Screens = [.. config.Screens.Keys],
            Frames = [.. config.Frames.Select(frame => new ControlFrame { Id = frame.Key, Label = frame.Value.Label, Snapshot = screenState.GetSnapshot(frame.Key) })],
        };
    }

    private static async Task<IResult> SetOverrideAsync(string frameId, OverrideRequest body, ConfigStore configStore, ScreenState screenState, CancellationToken cancellationToken)
    {
        var config = configStore.Current;
        if (!config.Frames.ContainsKey(frameId))
            return FrameNotFound(frameId);

        if (body.Screen is null || !config.Screens.ContainsKey(body.Screen))
            return Results.BadRequest($"Pass \"screen\" with one of: {string.Join(", ", config.Screens.Keys)}.");

        var minutes = body.Minutes ?? DefaultOverrideMinutes;
        if (minutes is < 1 or > MaxOverrideMinutes)
            return Results.BadRequest($"Pass \"minutes\" from 1 to {MaxOverrideMinutes}, or leave it out for {DefaultOverrideMinutes}.");

        await screenState.SetOverrideAsync(frameId, body.Screen, TimeSpan.FromMinutes(minutes), cancellationToken);
        return Results.Ok(screenState.GetSnapshot(frameId));
    }

    private static async Task<IResult> ClearOverrideAsync(string frameId, ConfigStore configStore, ScreenState screenState, CancellationToken cancellationToken)
    {
        if (!configStore.Current.Frames.ContainsKey(frameId))
            return FrameNotFound(frameId);

        await screenState.ClearOverrideAsync(frameId, cancellationToken);
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
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var errors = await configStore.ApplyAsync(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        if (errors.Count > 0)
            return Results.BadRequest(new { errors });

        screenState.Refresh();
        return Results.Ok(configStore.Current.Frames.Keys.Select(screenState.GetSnapshot));
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
