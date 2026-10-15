namespace FrameRouter.Screens;

internal static class ScheduleResolver
{
    public static string Resolve(FramePlan plan, DateTime localNow) =>
        plan.Schedule.FirstOrDefault(rule => Matches(rule, localNow))?.Screen ?? plan.Default;

    // 0 means no overlay.
    public static double ResolveDim(FramePlan plan, DateTime localNow) =>
        plan.Dim.FirstOrDefault(rule => Matches(rule, localNow))?.Level ?? 0;

    private static bool Matches(TimeWindow window, DateTime localNow)
    {
        var from = ScreenConfigParser.ParseTime(window.From);
        var to = ScreenConfigParser.ParseTime(window.To);
        var time = TimeOnly.FromDateTime(localNow);

        if (from < to)
            return time >= from && time < to && RunsOn(window, localNow.DayOfWeek);

        // The window passes midnight: "days" names the day the window starts on.
        if (time >= from)
            return RunsOn(window, localNow.DayOfWeek);

        return time < to && RunsOn(window, localNow.AddDays(-1).DayOfWeek);
    }

    private static bool RunsOn(TimeWindow window, DayOfWeek day) =>
        window.Days is not { Count: > 0 } days || days.Any(name => ScreenConfigParser.Days[name] == day);
}
