namespace FrameRouter.Screens;

internal static class ScheduleResolver
{
    public static string Resolve(FramePlan plan, DateTime localNow) =>
        plan.Schedule.FirstOrDefault(rule => Matches(rule, localNow))?.Screen ?? plan.Default;

    private static bool Matches(ScheduleRule rule, DateTime localNow)
    {
        var from = ScreenConfigParser.ParseTime(rule.From);
        var to = ScreenConfigParser.ParseTime(rule.To);
        var time = TimeOnly.FromDateTime(localNow);

        if (from < to)
            return time >= from && time < to && RunsOn(rule, localNow.DayOfWeek);

        // The window passes midnight: "days" names the day the window starts on.
        if (time >= from)
            return RunsOn(rule, localNow.DayOfWeek);

        return time < to && RunsOn(rule, localNow.AddDays(-1).DayOfWeek);
    }

    private static bool RunsOn(ScheduleRule rule, DayOfWeek day) =>
        rule.Days is not { Count: > 0 } days || days.Any(name => ScreenConfigParser.Days[name] == day);
}
