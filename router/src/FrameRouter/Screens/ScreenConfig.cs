namespace FrameRouter.Screens;

internal sealed record ScreenConfig
{
    public required Dictionary<string, ScreenDefinition> Screens { get; init; }

    public required Dictionary<string, FramePlan> Frames { get; init; }
}

internal sealed record FramePlan
{
    public required string Default { get; init; }

    public IReadOnlyList<ScheduleRule> Schedule { get; init; } = [];
}

internal sealed record ScreenDefinition
{
    public required string Type { get; init; }

    public string? Title { get; init; }

    public IReadOnlyList<int>? Stops { get; init; }

    public int? Limit { get; init; }

    public string? Url { get; init; }
}

internal sealed record ScheduleRule
{
    public required string Screen { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }

    public IReadOnlyList<string>? Days { get; init; }
}

internal static class ScreenTypes
{
    public const string Clock = "clock";
    public const string Departures = "departures";
    public const string Url = "url";

    public static readonly IReadOnlySet<string> All = new HashSet<string>([Clock, Departures, Url], StringComparer.Ordinal);
}
