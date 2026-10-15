using FrameRouter.Screens;

namespace FrameRouter.Tests;

public sealed class ScreenConfigParserTests
{
    [Fact]
    public void Parse_RepositoryScreensJson_IsValid()
    {
        var result = ScreenConfigParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "screens.json")));

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Config);
    }

    [Fact]
    public void Parse_CommentsAndTrailingCommas_AreAccepted()
    {
        const string json = """
            {
              // a comment
              "screens": { "clock": { "type": "clock" }, },
              "frames": { "kitchen": { "default": "clock" }, },
            }
            """;

        var result = ScreenConfigParser.Parse(json);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_MisspelledKey_IsRejected()
    {
        const string json = """{ "screens": { "clock": { "type": "clock" } }, "frames": { "kitchen": { "defualt": "clock" } } }""";

        var result = ScreenConfigParser.Parse(json);

        Assert.Null(result.Config);
        Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData("""{ "type": "weather" }""", "unknown type")]
    [InlineData("""{ "type": "departures", "stops": [] }""", "at least one MZK busStopId")]
    [InlineData("""{ "type": "departures", "stops": [11], "limit": 50 }""", "\"limit\" must be between")]
    [InlineData("""{ "type": "url", "url": "ftp://example.org" }""", "absolute https")]
    [InlineData("""{ "type": "url", "url": "http://ha.local" }""", "absolute https")]
    public void Parse_InvalidScreen_ReportsError(string screen, string expected)
    {
        var json = $$"""{ "screens": { "clock": { "type": "clock" }, "bad": {{screen}} }, "frames": { "kitchen": { "default": "clock" } } }""";

        var result = ScreenConfigParser.Parse(json);

        Assert.Null(result.Config);
        Assert.Contains(result.Errors, error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "default": "missing" }""", "not in \"screens\"")]
    [InlineData("""{ "default": "clock", "schedule": [{ "screen": "clock", "from": "6:30", "to": "09:00" }] }""", "times like")]
    [InlineData("""{ "default": "clock", "schedule": [{ "screen": "clock", "from": "09:00", "to": "09:00" }] }""", "the same time")]
    [InlineData("""{ "default": "clock", "schedule": [{ "screen": "clock", "from": "06:00", "to": "09:00", "days": ["monday"] }] }""", "unknown day")]
    [InlineData("""{ "default": "clock", "dim": [{ "from": "23:00", "to": "06:30", "level": 0 }] }""", "\"level\" must be above 0")]
    [InlineData("""{ "default": "clock", "dim": [{ "from": "23:00", "to": "06:30", "level": 1.5 }] }""", "\"level\" must be above 0")]
    [InlineData("""{ "default": "clock", "dim": [{ "from": "23:00", "to": "6:30", "level": 0.8 }] }""", "dim rule 1: \"from\" and \"to\" must be times")]
    [InlineData("""{ "default": "clock", "dim": [{ "from": "23:00", "to": "06:30", "level": 0.8, "days": ["xyz"] }] }""", "unknown day")]
    [InlineData("""{ "default": "clock", "dim": [{ "from": "23:00", "to": "06:30" }] }""", "level")]
    public void Parse_InvalidFramePlan_ReportsError(string plan, string expected)
    {
        var json = $$"""{ "screens": { "clock": { "type": "clock" } }, "frames": { "kitchen": {{plan}} } }""";

        var result = ScreenConfigParser.Parse(json);

        Assert.Null(result.Config);
        Assert.Contains(result.Errors, error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "screens": { "clock": { "type": "clock" } }, "frames": { "kitchen": { "default": null } } }""")]
    [InlineData("""{ "screens": { "clock": { "type": "clock" } }, "frames": { "kitchen": { "default": "clock", "schedule": null } } }""")]
    [InlineData("""{ "screens": { "clock": null }, "frames": { "kitchen": { "default": "clock" } } }""")]
    [InlineData("""{ "screens": { "clock": { "type": "clock" } }, "frames": { "kitchen": null } }""")]
    [InlineData("""{ "screens": { "clock": { "type": "clock" } }, "frames": { "kitchen": { "default": "clock", "schedule": [null] } } }""")]
    [InlineData("""{ "screens": { "clock": { "type": "clock" } }, "frames": { "kitchen": { "default": "clock", "dim": null } } }""")]
    [InlineData("""{ "screens": { "clock": { "type": "clock" } }, "frames": { "kitchen": { "default": "clock", "dim": [null] } } }""")]
    [InlineData("""{ "screens": { "clock": { "type": "clock" } }, "frames": { "kitchen": { "default": "clock", "schedule": [{ "screen": "clock", "from": "06:00", "to": "07:00", "days": [null] }] } } }""")]
    public void Parse_NullValue_ReportsErrorWithoutThrowing(string json)
    {
        var result = ScreenConfigParser.Parse(json);

        Assert.Null(result.Config);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("kitchen", true)]
    [InlineData("frame-2", true)]
    [InlineData("Kitchen", false)]
    [InlineData("", false)]
    [InlineData("a/b", false)]
    public void IsValidFrameId_Input_MatchesRule(string frameId, bool expected) =>
        Assert.Equal(expected, ScreenConfigParser.IsValidFrameId(frameId));
}
