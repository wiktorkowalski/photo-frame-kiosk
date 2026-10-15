namespace FrameRouter.Screens;

internal sealed record ScreenConfig
{
    public required Dictionary<string, ScreenDefinition> Screens { get; init; }

    public required Dictionary<string, FramePlan> Frames { get; init; }
}

internal sealed record FramePlan
{
    // Shown on the control page, for example "Kuchnia". The frame id is shown when it is missing.
    public string? Label { get; init; }

    public required string Default { get; init; }

    public IReadOnlyList<ScheduleRule> Schedule { get; init; } = [];

    public IReadOnlyList<DimRule> Dim { get; init; } = [];
}

internal sealed record ScreenDefinition
{
    public required string Type { get; init; }

    public string? Title { get; init; }

    public IReadOnlyList<int>? Stops { get; init; }

    public int? Limit { get; init; }

    public string? Url { get; init; }
}

// A daily time window in Europe/Warsaw. "days" names the day the window starts on.
internal abstract record TimeWindow
{
    public required string From { get; init; }

    public required string To { get; init; }

    public IReadOnlyList<string>? Days { get; init; }
}

internal sealed record ScheduleRule : TimeWindow
{
    public required string Screen { get; init; }
}

// Level is the opacity of the black overlay: 0.85 leaves 15 % of the brightness.
internal sealed record DimRule : TimeWindow
{
    public required double Level { get; init; }
}

internal static class ScreenTypes
{
    public const string Clock = "clock";
    public const string Departures = "departures";
    public const string Url = "url";

    public static readonly IReadOnlySet<string> All = new HashSet<string>([Clock, Departures, Url], StringComparer.Ordinal);
}
