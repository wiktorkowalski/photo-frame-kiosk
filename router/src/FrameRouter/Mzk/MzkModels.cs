namespace FrameRouter.Mzk;

internal sealed record Departure
{
    public required int StopId { get; init; }

    public required string Line { get; init; }

    public required string Direction { get; init; }

    // MZK's own display text: "3 min", "<1min" or a clock time like "14:43".
    public required string Time { get; init; }

    public required int Seconds { get; init; }

    // True when a GPS-tracked vehicle runs the course, so the time is live, not timetable.
    public required bool Live { get; init; }

    // Set when the departure is not today, as yyyy-MM-dd.
    public string? Date { get; init; }
}

internal sealed record StopBoard
{
    public required int StopId { get; init; }

    public required string StopName { get; init; }

    public string? Notice { get; init; }

    public required IReadOnlyList<Departure> Departures { get; init; }
}

internal sealed record StopMatch
{
    public required int StopId { get; init; }

    public required string Name { get; init; }

    public required string Number { get; init; }
}

internal sealed record StopSummary
{
    public required int StopId { get; init; }

    public required string StopName { get; init; }

    public string? Notice { get; init; }
}

internal sealed record DepartureBoard
{
    public required IReadOnlyList<StopSummary> Stops { get; init; }

    public required IReadOnlyList<Departure> Departures { get; init; }

    public required IReadOnlyList<int> FailedStops { get; init; }

    public required DateTimeOffset FetchedAt { get; init; }
}

internal sealed record StopOption
{
    public required int StopId { get; init; }

    public required string Name { get; init; }

    public required string Number { get; init; }

    public required IReadOnlyList<string> Lines { get; init; }

    public required IReadOnlyList<string> Directions { get; init; }
}
