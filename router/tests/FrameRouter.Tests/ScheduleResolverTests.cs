using FrameRouter.Screens;

namespace FrameRouter.Tests;

public sealed class ScheduleResolverTests
{
    // 2026-10-15 is a Thursday.
    private static readonly DateTime Thursday = new(2026, 10, 15);

    private static FramePlan Plan(params ScheduleRule[] rules) => new() { Default = "clock", Schedule = rules };

    private static FramePlan DimPlan(params DimRule[] rules) => new() { Default = "clock", Dim = rules };

    private static DimRule Dim(double level, string from, string to, params string[] days) =>
        new() { Level = level, From = from, To = to, Days = days.Length == 0 ? null : days };

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

    [Theory]
    [InlineData(22, 59, 0)]
    [InlineData(23, 0, 0.85)]
    [InlineData(30, 29, 0.85)]
    [InlineData(30, 30, 0)]
    public void ResolveDim_WindowPastMidnight_StartInclusiveEndExclusive(int hour, int minute, double expected)
    {
        var plan = DimPlan(Dim(0.85, "23:00", "06:30"));

        Assert.Equal(expected, ScheduleResolver.ResolveDim(plan, Thursday.AddHours(hour).AddMinutes(minute)));
    }

    [Fact]
    public void ResolveDim_NoRules_IsZero() =>
        Assert.Equal(0, ScheduleResolver.ResolveDim(DimPlan(), Thursday.AddHours(23)));

    [Fact]
    public void ResolveDim_DayNotListed_IsZero()
    {
        var plan = DimPlan(Dim(0.85, "23:00", "06:30", "fri", "sat"));

        Assert.Equal(0, ScheduleResolver.ResolveDim(plan, Thursday.AddHours(23)));
        Assert.Equal(0.85, ScheduleResolver.ResolveDim(plan, Thursday.AddDays(1).AddHours(23)));
    }

    [Fact]
    public void ResolveDim_OverlappingRules_FirstRuleWins()
    {
        var plan = DimPlan(Dim(0.95, "01:00", "05:00"), Dim(0.7, "22:00", "07:00"));

        Assert.Equal(0.95, ScheduleResolver.ResolveDim(plan, Thursday.AddHours(3)));
        Assert.Equal(0.7, ScheduleResolver.ResolveDim(plan, Thursday.AddHours(6)));
    }
}
