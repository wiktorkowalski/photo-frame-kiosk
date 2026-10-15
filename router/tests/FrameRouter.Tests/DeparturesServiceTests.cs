using FrameRouter.Mzk;

namespace FrameRouter.Tests;

public sealed class DeparturesServiceTests
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static StopBoard Board(int stopId, params int[] seconds) => new()
    {
        StopId = stopId,
        StopName = $"Stop {stopId}",
        Departures = seconds
            .Select(value => new Departure { StopId = stopId, Line = "1", Direction = "X", Time = "t", Seconds = value, Live = false })
            .ToList(),
    };

    [Fact]
    public void BuildBoard_TwoStops_MergesSortsAndLimits()
    {
        var board = DeparturesService.BuildBoard([11, 28], [Board(11, 300, 60), Board(28, 120)], 2, FetchedAt);

        Assert.Equal([60, 120], board.Departures.Select(departure => departure.Seconds));
        Assert.Equal([11, 28], board.Stops.Select(stop => stop.StopId));
        Assert.Empty(board.FailedStops);
    }

    [Fact]
    public void BuildBoard_OneStopFailed_ListsItAsFailed()
    {
        var board = DeparturesService.BuildBoard([11, 28], [Board(11, 60), null], 8, FetchedAt);

        Assert.Equal([28], board.FailedStops);
        Assert.Single(board.Stops);
    }
}
