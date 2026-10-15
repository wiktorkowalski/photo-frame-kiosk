using System.Text.Json;
using System.Xml;

namespace FrameRouter.Mzk;

internal sealed class DeparturesService(MzkClient mzkClient, TimeProvider timeProvider, ILogger<DeparturesService> logger)
{
    public const int MaxStopsPerRequest = 6;
    private const int MaxStopOptions = 12;
    private const int StopLookupParallelism = 4;

    public async Task<DepartureBoard> GetBoardAsync(IReadOnlyList<int> stopIds, int limit, CancellationToken cancellationToken)
    {
        var boards = await Task.WhenAll(stopIds.Select(stopId => TryGetTimetableAsync(stopId, cancellationToken)));
        return BuildBoard(stopIds, boards, limit, timeProvider.GetUtcNow());
    }

    // Null when MZK's stop search fails.
    public async Task<IReadOnlyList<StopOption>?> FindStopsAsync(string query, CancellationToken cancellationToken)
    {
        List<StopMatch> matches;
        try
        {
            matches = (await mzkClient.SearchStopsAsync(query, cancellationToken)).Take(MaxStopOptions).ToList();
        }
        catch (Exception ex) when (IsMzkFailure(ex, cancellationToken))
        {
            logger.LogWarning(ex, "MZK stop search for {StopQuery} failed", query);
            return null;
        }

        var options = new StopOption[matches.Count];

        // Lines and directions are not in the stop list; only a timetable call shows which way a post goes.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, matches.Count),
            new ParallelOptions { MaxDegreeOfParallelism = StopLookupParallelism, CancellationToken = cancellationToken },
            async (index, token) => options[index] = ToOption(matches[index], await TryGetTimetableAsync(matches[index].StopId, token)));

        return options;
    }

    internal static DepartureBoard BuildBoard(IReadOnlyList<int> stopIds, IReadOnlyList<StopBoard?> boards, int limit, DateTimeOffset fetchedAt)
    {
        var loaded = boards.OfType<StopBoard>().ToList();
        return new DepartureBoard
        {
            Stops = loaded.Select(board => new StopSummary { StopId = board.StopId, StopName = board.StopName, Notice = board.Notice }).ToList(),
            Departures = loaded.SelectMany(board => board.Departures).OrderBy(departure => departure.Seconds).Take(limit).ToList(),
            FailedStops = stopIds.Where(stopId => loaded.All(board => board.StopId != stopId)).ToList(),
            FetchedAt = fetchedAt,
        };
    }

    private static StopOption ToOption(StopMatch match, StopBoard? board) => new()
    {
        StopId = match.StopId,
        Name = match.Name,
        Number = match.Number,
        Lines = board?.Departures.Select(departure => departure.Line).Distinct(StringComparer.Ordinal).ToList() ?? [],
        Directions = board?.Departures.Select(departure => departure.Direction).Distinct(StringComparer.Ordinal).ToList() ?? [],
    };

    private async Task<StopBoard?> TryGetTimetableAsync(int stopId, CancellationToken cancellationToken)
    {
        try
        {
            return await mzkClient.GetTimetableAsync(stopId, cancellationToken);
        }
        catch (Exception ex) when (IsMzkFailure(ex, cancellationToken))
        {
            logger.LogWarning(ex, "MZK timetable for stop {StopId} failed", stopId);
            return null;
        }
    }

    // Timeouts count, a cancelled request does not. Parse errors cover a changed response format.
    private static bool IsMzkFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or XmlException or JsonException or FormatException or InvalidOperationException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
