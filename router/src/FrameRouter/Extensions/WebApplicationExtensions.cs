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
        app.MapPost("/api/config", ApplyConfigAsync);

        return app;
    }

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

    private static async Task<IResult> ApplyConfigAsync(HttpRequest request, ConfigStore configStore, ScreenState screenState, IOptions<FrameRouterOptions> options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.Value.ConfigToken))
            return Results.Problem("Config push is off: FrameRouter__ConfigToken is not set.", statusCode: StatusCodes.Status403Forbidden);

        if (!IsAuthorized(request.Headers.Authorization.ToString(), options.Value.ConfigToken))
            return Results.Unauthorized();

        // Kestrel answers 413 itself when the body passes the limit, chunked or not.
        request.HttpContext.Features.GetRequiredFeature<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = MaxConfigBytes;

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var errors = await configStore.ApplyAsync(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        if (errors.Count > 0)
            return Results.BadRequest(new { errors });

        screenState.Refresh();
        return Results.Ok(configStore.Current.Frames.Keys.Select(screenState.GetSnapshot));
    }

    private static bool IsAuthorized(string header, string token)
    {
        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.Ordinal))
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[scheme.Length..]), Encoding.UTF8.GetBytes(token));
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
