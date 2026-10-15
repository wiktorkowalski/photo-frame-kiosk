using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace FrameRouter.Screens;

// Screen is null when screens.json has no entry for the frame.
internal sealed record ScreenSnapshot(string Frame, string? Name, ScreenDefinition? Screen, string Version);

internal sealed class ScreenState(ConfigStore configStore, TimeProvider timeProvider, IOptions<FrameRouterOptions> options, ILogger<ScreenState> logger)
{
    public const string EventType = "screen";

    // The frame page reconnects when it hears nothing for 70 s, so the heartbeat must come well before that.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
    private readonly Lock _gate = new();
    private Dictionary<string, ScreenSnapshot>? _snapshots;
    private TaskCompletionSource _changed = NewSignal();

    public ScreenSnapshot GetSnapshot(string frameId)
    {
        lock (_gate)
            return Lookup(frameId);
    }

    public void Refresh()
    {
        lock (_gate)
            RefreshLocked();
    }

    public async IAsyncEnumerable<SseItem<ScreenSnapshot>> StreamAsync(string frameId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ScreenSnapshot snapshot;
            Task changed;
            lock (_gate)
            {
                snapshot = Lookup(frameId);
                changed = _changed.Task;
            }

            // Every event carries the full snapshot, so the heartbeat doubles as a resync.
            yield return new SseItem<ScreenSnapshot>(snapshot, EventType) { ReconnectionInterval = ReconnectDelay };
            await Task.WhenAny(changed, Task.Delay(HeartbeatInterval, timeProvider, cancellationToken));
        }
    }

    private ScreenSnapshot Lookup(string frameId)
    {
        if (_snapshots is null)
            RefreshLocked();

        return _snapshots!.TryGetValue(frameId, out var snapshot)
            ? snapshot
            : new ScreenSnapshot(frameId, null, null, AppVersion.Value);
    }

    private void RefreshLocked()
    {
        var config = configStore.Current;
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), _timeZone).DateTime;
        var next = config.Frames.ToDictionary(
            frame => frame.Key,
            frame => Resolve(config, frame.Key, frame.Value, localNow),
            StringComparer.Ordinal);

        var changedFrames = next.Values.Where(snapshot => _snapshots?.GetValueOrDefault(snapshot.Frame) != snapshot).ToList();
        if (changedFrames.Count == 0 && _snapshots?.Count == next.Count)
            return;

        foreach (var snapshot in changedFrames)
            logger.LogInformation("Frame {FrameId} now shows screen {ScreenName}", snapshot.Frame, snapshot.Name);

        _snapshots = next;
        var previous = _changed;
        _changed = NewSignal();
        previous.SetResult();
    }

    private static ScreenSnapshot Resolve(ScreenConfig config, string frameId, FramePlan plan, DateTime localNow)
    {
        var name = ScheduleResolver.Resolve(plan, localNow);
        return new ScreenSnapshot(frameId, name, config.Screens[name], AppVersion.Value);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
