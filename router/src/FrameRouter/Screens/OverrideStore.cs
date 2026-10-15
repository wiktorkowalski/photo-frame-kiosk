using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FrameRouter.Screens;

// A screen set from the control page that wins over the schedule until Until (UTC).
internal sealed record ScreenOverride
{
    public required string Screen { get; init; }

    public required DateTimeOffset Until { get; init; }
}

// Temporary per-frame overrides. screens.json stays the baseline: an override ends on its own
// and a config push does not clear it. Saved to the data directory so a restart keeps it.
internal sealed class OverrideStore(IOptions<FrameRouterOptions> options, TimeProvider timeProvider, ILogger<OverrideStore> logger)
{
    private const string FileName = "overrides.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path = Path.Combine(options.Value.DataDirectory, FileName);
    private readonly Lock _gate = new();
    private Dictionary<string, ScreenOverride> _overrides = new(StringComparer.Ordinal);

    public void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, ScreenOverride>>(File.ReadAllText(_path), JsonOptions) ?? [];
            lock (_gate)
                _overrides = Unexpired(saved);
            logger.LogInformation("Loaded {OverrideCount} active screen overrides from {OverridePath}", _overrides.Count, _path);
        }
        catch (JsonException ex)
        {
            // Overrides are short-lived; losing them is better than refusing to start.
            logger.LogError(ex, "The saved overrides {OverridePath} are invalid, so the router starts without overrides", _path);
        }
    }

    public ScreenOverride? GetActive(string frameId)
    {
        lock (_gate)
            return _overrides.TryGetValue(frameId, out var screenOverride) && screenOverride.Until > timeProvider.GetUtcNow() ? screenOverride : null;
    }

    public void Set(string frameId, string screen, TimeSpan duration) =>
        Update(overrides => overrides[frameId] = new ScreenOverride { Screen = screen, Until = timeProvider.GetUtcNow() + duration });

    public void Clear(string frameId) =>
        Update(overrides => overrides.Remove(frameId));

    private void Update(Action<Dictionary<string, ScreenOverride>> change)
    {
        lock (_gate)
        {
            var next = Unexpired(_overrides);
            change(next);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(next, JsonOptions));
            _overrides = next;
        }
    }

    // Also drops null entries from a hand-edited file.
    private Dictionary<string, ScreenOverride> Unexpired(Dictionary<string, ScreenOverride> overrides)
    {
        var now = timeProvider.GetUtcNow();
        return overrides.Where(entry => entry.Value is not null && entry.Value.Until > now).ToDictionary(StringComparer.Ordinal);
    }
}
