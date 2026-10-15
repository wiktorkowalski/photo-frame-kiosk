using FrameRouter.Screens;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FrameRouter.Tests;

public sealed class ScreenStateTests : IDisposable
{
    private const string Config = """
        {
          "screens": { "clock": { "type": "clock" }, "departures": { "type": "departures", "stops": [11] } },
          "frames": {
            "kitchen": { "default": "clock", "schedule": [{ "screen": "departures", "from": "06:30", "to": "09:00" }] },
            "hall": { "default": "departures" }
          }
        }
        """;

    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("frame-router-tests").FullName;
    // 06:29:50 in Warsaw (UTC+2 in October), 10 s before the kitchen's departures window.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 4, 29, 50, TimeSpan.Zero));
    private readonly ConfigStore _configStore;
    private readonly ScreenState _screenState;

    public ScreenStateTests()
    {
        var options = Options.Create(new FrameRouterOptions { DataDirectory = _dataDirectory });
        _configStore = new ConfigStore(options, NullLogger<ConfigStore>.Instance);
        _screenState = new ScreenState(_configStore, _time, options, NullLogger<ScreenState>.Instance);
    }

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    [Fact]
    public async Task GetSnapshot_ScheduleWindowStarts_SwitchesAfterRefreshAsync()
    {
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));
        Assert.Equal("clock", _screenState.GetSnapshot("kitchen").Name);

        _time.Advance(TimeSpan.FromMinutes(30));
        _screenState.Refresh();

        Assert.Equal("departures", _screenState.GetSnapshot("kitchen").Name);
        Assert.Equal("departures", _screenState.GetSnapshot("hall").Name);
    }

    [Fact]
    public async Task GetSnapshot_UnknownFrame_HasNoScreenAsync()
    {
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));

        var snapshot = _screenState.GetSnapshot("garage");

        Assert.Null(snapshot.Screen);
        Assert.Equal("garage", snapshot.Frame);
    }

    [Fact]
    public async Task StreamAsync_ScreenChanges_YieldsTheNewSnapshotAsync()
    {
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = _screenState.StreamAsync("kitchen", cancellation.Token, CancellationToken.None).GetAsyncEnumerator(cancellation.Token);

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("clock", stream.Current.Data.Name);

        var next = stream.MoveNextAsync();
        // Less than the 25 s heartbeat, so the next item comes from the change, not the heartbeat.
        _time.Advance(TimeSpan.FromSeconds(10));
        _screenState.Refresh();

        Assert.True(await next);
        Assert.Equal("departures", stream.Current.Data.Name);
    }

    [Fact]
    public async Task ApplyAsync_InvalidConfig_KeepsThePreviousOneAsync()
    {
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));

        var errors = await _configStore.ApplyAsync("""{ "screens": {}, "frames": {} }""", CancellationToken.None);

        Assert.NotEmpty(errors);
        Assert.Equal(2, _configStore.Current.Frames.Count);
    }

    [Fact]
    public async Task Load_AfterApply_ReadsTheSavedConfigAsync()
    {
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));
        var restarted = new ConfigStore(Options.Create(new FrameRouterOptions { DataDirectory = _dataDirectory }), NullLogger<ConfigStore>.Instance);

        restarted.Load();

        Assert.Contains("hall", restarted.Current.Frames.Keys);
    }
}
