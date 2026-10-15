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
            "kitchen": {
              "default": "clock",
              "schedule": [{ "screen": "departures", "from": "06:30", "to": "09:00" }],
              "dim": [{ "from": "23:00", "to": "06:30", "level": 0.85 }]
            },
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
        _screenState = new ScreenState(_configStore, CreateOverrideStore(), _time, options, NullLogger<ScreenState>.Instance);
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
    public async Task GetSnapshot_DimWindowEnds_ClearsDimAndKeepsTheScreenRulesAsync()
    {
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));
        Assert.Equal(0.85, _screenState.GetSnapshot("kitchen").Dim);
        Assert.Equal(0, _screenState.GetSnapshot("hall").Dim);

        _time.Advance(TimeSpan.FromMinutes(1));
        _screenState.Refresh();

        var snapshot = _screenState.GetSnapshot("kitchen");
        Assert.Equal(0, snapshot.Dim);
        Assert.Equal("departures", snapshot.Name);
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
    public async Task StreamAsync_OnlyDimChanges_YieldsTheNewSnapshotAsync()
    {
        const string dimOnly = """
            {
              "screens": { "clock": { "type": "clock" } },
              "frames": { "kitchen": { "default": "clock", "dim": [{ "from": "06:30", "to": "07:00", "level": 0.5 }] } }
            }
            """;
        Assert.Empty(await _configStore.ApplyAsync(dimOnly, CancellationToken.None));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = _screenState.StreamAsync("kitchen", cancellation.Token, CancellationToken.None).GetAsyncEnumerator(cancellation.Token);

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(0, stream.Current.Data.Dim);

        var next = stream.MoveNextAsync();
        _time.Advance(TimeSpan.FromSeconds(10));
        _screenState.Refresh();

        Assert.True(await next);
        Assert.Equal(0.5, stream.Current.Data.Dim);
        Assert.Equal("clock", stream.Current.Data.Name);
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

    [Fact]
    public async Task GetSnapshot_OverrideActive_WinsOverTheScheduleUntilItEndsAsync()
    {
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));

        await _screenState.SetOverrideAsync("kitchen", "departures", TimeSpan.FromMinutes(30), CancellationToken.None);

        var overridden = _screenState.GetSnapshot("kitchen");
        Assert.Equal("departures", overridden.Name);
        Assert.Equal(_time.GetUtcNow().AddMinutes(30), overridden.OverrideUntil);
        Assert.Equal(0.85, overridden.Dim);
        Assert.Null(_screenState.GetSnapshot("hall").OverrideUntil);

        // The schedule says departures from 06:30 to 09:00; this override says clock until 07:00.
        await _screenState.SetOverrideAsync("kitchen", "clock", TimeSpan.FromMinutes(30), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(29));
        _screenState.Refresh();
        Assert.Equal("clock", _screenState.GetSnapshot("kitchen").Name);

        _time.Advance(TimeSpan.FromMinutes(1));
        _screenState.Refresh();

        var expired = _screenState.GetSnapshot("kitchen");
        Assert.Equal("departures", expired.Name);
        Assert.Null(expired.OverrideUntil);
    }

    [Fact]
    public async Task GetSnapshot_OverrideCleared_ReturnsToTheScheduleAsync()
    {
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));
        await _screenState.SetOverrideAsync("hall", "clock", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Equal("clock", _screenState.GetSnapshot("hall").Name);

        await _screenState.ClearOverrideAsync("hall", CancellationToken.None);

        Assert.Equal("departures", _screenState.GetSnapshot("hall").Name);
    }

    [Fact]
    public async Task GetSnapshot_PushKeepsTheOverrideScreen_KeepsTheOverrideAsync()
    {
        const string hallOnClock = """
            { "screens": { "clock": { "type": "clock" }, "departures": { "type": "departures", "stops": [11] } }, "frames": { "hall": { "default": "clock" } } }
            """;
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));
        await _screenState.SetOverrideAsync("hall", "departures", TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Empty(await _configStore.ApplyAsync(hallOnClock, CancellationToken.None));
        _screenState.Refresh();

        var snapshot = _screenState.GetSnapshot("hall");
        Assert.Equal("departures", snapshot.Name);
        Assert.NotNull(snapshot.OverrideUntil);
    }

    [Fact]
    public async Task GetSnapshot_PushRemovesTheOverrideScreen_FallsBackToTheScheduleAsync()
    {
        const string withoutClock = """
            { "screens": { "departures": { "type": "departures", "stops": [11] } }, "frames": { "hall": { "default": "departures" } } }
            """;
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));
        await _screenState.SetOverrideAsync("hall", "clock", TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Empty(await _configStore.ApplyAsync(withoutClock, CancellationToken.None));
        _screenState.Refresh();

        var snapshot = _screenState.GetSnapshot("hall");
        Assert.Equal("departures", snapshot.Name);
        Assert.Null(snapshot.OverrideUntil);
    }

    [Fact]
    public async Task StreamAsync_OverrideSet_YieldsTheNewSnapshotAsync()
    {
        Assert.Empty(await _configStore.ApplyAsync(Config, CancellationToken.None));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = _screenState.StreamAsync("hall", cancellation.Token, CancellationToken.None).GetAsyncEnumerator(cancellation.Token);
        Assert.True(await stream.MoveNextAsync());

        var next = stream.MoveNextAsync();
        await _screenState.SetOverrideAsync("hall", "clock", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.True(await next);
        Assert.Equal("clock", stream.Current.Data.Name);
    }

    [Fact]
    public async Task Load_AfterRestart_KeepsActiveOverridesAndDropsExpiredOnesAsync()
    {
        var store = CreateOverrideStore();
        await store.SetAsync("kitchen", "clock", TimeSpan.FromMinutes(10), CancellationToken.None);
        await store.SetAsync("hall", "clock", TimeSpan.FromMinutes(60), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(30));
        var restarted = CreateOverrideStore();

        restarted.Load();

        Assert.Null(restarted.GetActive("kitchen"));
        Assert.Equal("clock", restarted.GetActive("hall")?.Screen);
    }

    private OverrideStore CreateOverrideStore() =>
        new(Options.Create(new FrameRouterOptions { DataDirectory = _dataDirectory }), _time, NullLogger<OverrideStore>.Instance);
}
