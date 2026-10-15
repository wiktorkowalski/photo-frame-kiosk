using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FrameRouter.Screens;

internal sealed record ScreenOverride
{
    public required string Screen { get; init; }

    public required DateTimeOffset Until { get; init; }
}

// screens.json stays the baseline: an override ends on its own, and a config push does not clear it.
internal sealed class OverrideStore(IOptions<FrameRouterOptions> options, TimeProvider timeProvider, ILogger<OverrideStore> logger)
{
    private const string FileName = "overrides.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path = Path.Combine(options.Value.DataDirectory, FileName);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    // Replaced, never mutated: readers on the schedule tick need no lock.
    private Dictionary<string, ScreenOverride> _overrides = new(StringComparer.Ordinal);

    public void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            var saved = Unexpired(JsonSerializer.Deserialize<Dictionary<string, ScreenOverride>>(File.ReadAllText(_path), JsonOptions) ?? []);
            Volatile.Write(ref _overrides, saved);
            logger.LogInformation("Loaded {OverrideCount} active screen overrides from {OverridePath}", saved.Count, _path);
        }
        catch (JsonException ex)
        {
            // Overrides are short-lived; losing them is better than refusing to start.
            logger.LogError(ex, "The saved overrides {OverridePath} are invalid, so the router starts without overrides", _path);
        }
    }

    public ScreenOverride? GetActive(string frameId) =>
        Volatile.Read(ref _overrides).TryGetValue(frameId, out var screenOverride) && screenOverride.Until > timeProvider.GetUtcNow()
            ? screenOverride
            : null;

    public Task SetAsync(string frameId, string screen, TimeSpan duration, CancellationToken cancellationToken) =>
        UpdateAsync(overrides => overrides[frameId] = new ScreenOverride { Screen = screen, Until = timeProvider.GetUtcNow() + duration }, cancellationToken);

    public Task ClearAsync(string frameId, CancellationToken cancellationToken) =>
        UpdateAsync(overrides => overrides.Remove(frameId), cancellationToken);

    private async Task UpdateAsync(Action<Dictionary<string, ScreenOverride>> change, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var next = Unexpired(_overrides);
            change(next);
            await AtomicFile.WriteAllTextAsync(_path, JsonSerializer.Serialize(next, JsonOptions), cancellationToken);
            Volatile.Write(ref _overrides, next);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // Also drops null entries from a hand-edited file.
    private Dictionary<string, ScreenOverride> Unexpired(Dictionary<string, ScreenOverride> overrides)
    {
        var now = timeProvider.GetUtcNow();
        return overrides.Where(entry => entry.Value is not null && entry.Value.Until > now).ToDictionary(StringComparer.Ordinal);
    }
}
