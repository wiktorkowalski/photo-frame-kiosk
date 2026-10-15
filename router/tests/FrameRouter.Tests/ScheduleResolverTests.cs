using FrameRouter.Screens;

namespace FrameRouter.Tests;

public sealed class ScheduleResolverTests
{
    // 2026-10-15 is a Thursday.
    private static readonly DateTime Thursday = new(2026, 10, 15);

    private static FramePlan Plan(params ScheduleRule[] rules) => new() { Default = "clock", Schedule = rules };

    private static ScheduleRule Rule(string screen, string from, string to, params string[] days) =>
        new() { Screen = screen, From = from, To = to, Days = days.Length == 0 ? null : days };

    [Theory]
    [InlineData(6, 29, "clock")]
    [InlineData(6, 30, "departures")]
    [InlineData(8, 59, "departures")]
    [InlineData(9, 0, "clock")]
    public void Resolve_SameDayWindow_StartInclusiveEndExclusive(int hour, int minute, string expected)
    {
        var plan = Plan(Rule("departures", "06:30", "09:00"));

        Assert.Equal(expected, ScheduleResolver.Resolve(plan, Thursday.AddHours(hour).AddMinutes(minute)));
    }

    [Fact]
    public void Resolve_DayNotListed_FallsBackToDefault()
    {
        var plan = Plan(Rule("departures", "06:30", "09:00", "sat", "sun"));

        Assert.Equal("clock", ScheduleResolver.Resolve(plan, Thursday.AddHours(7)));
    }

    [Fact]
    public void Resolve_WindowPastMidnight_UsesTheDayItStarted()
    {
        var plan = Plan(Rule("night", "22:00", "06:00", "thu"));

        Assert.Equal("night", ScheduleResolver.Resolve(plan, Thursday.AddHours(23)));
        Assert.Equal("night", ScheduleResolver.Resolve(plan, Thursday.AddDays(1).AddHours(5)));
        Assert.Equal("clock", ScheduleResolver.Resolve(plan, Thursday.AddHours(5)));
    }

    [Fact]
    public void Resolve_OverlappingRules_FirstRuleWins()
    {
        var plan = Plan(Rule("first", "06:00", "10:00"), Rule("second", "07:00", "08:00"));

        Assert.Equal("first", ScheduleResolver.Resolve(plan, Thursday.AddHours(7.5)));
    }
}
