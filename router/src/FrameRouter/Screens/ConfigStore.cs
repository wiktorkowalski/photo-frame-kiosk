using Microsoft.Extensions.Options;

namespace FrameRouter.Screens;

internal sealed class ConfigStore(IOptions<FrameRouterOptions> options, ILogger<ConfigStore> logger)
{
    private const string FileName = "screens.json";

    private readonly string _savedPath = Path.Combine(options.Value.DataDirectory, FileName);
    private readonly string _builtInPath = Path.Combine(AppContext.BaseDirectory, FileName);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private ScreenConfig? _current;

    public ScreenConfig Current => Volatile.Read(ref _current) ?? throw new InvalidOperationException("ConfigStore.Load has not run.");

    public void Load()
    {
        if (File.Exists(_savedPath))
        {
            var saved = ScreenConfigParser.Parse(File.ReadAllText(_savedPath));
            if (saved.Config is not null)
            {
                Volatile.Write(ref _current, saved.Config);
                logger.LogInformation("Loaded the last pushed screen config from {ConfigPath}", _savedPath);
                return;
            }

            logger.LogError("The saved screen config {ConfigPath} is invalid, so the router uses the built-in one: {ConfigErrors}", _savedPath, saved.Errors);
        }

        var builtIn = ScreenConfigParser.Parse(File.ReadAllText(_builtInPath));
        if (builtIn.Config is null)
            throw new InvalidOperationException($"The built-in screen config {_builtInPath} is invalid: {string.Join(" ", builtIn.Errors)}");

        Volatile.Write(ref _current, builtIn.Config);
        logger.LogInformation("Loaded the built-in screen config from {ConfigPath}", _builtInPath);
    }

    public async Task<IReadOnlyList<string>> ApplyAsync(string json, CancellationToken cancellationToken)
    {
        var result = ScreenConfigParser.Parse(json);
        if (result.Config is null)
        {
            logger.LogWarning("Rejected a pushed screen config: {ConfigErrors}", result.Errors);
            return result.Errors;
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            AtomicFile.WriteAllText(_savedPath, json);
            Volatile.Write(ref _current, result.Config);
        }
        finally
        {
            _writeGate.Release();
        }

        logger.LogInformation("Applied a pushed screen config with {FrameCount} frames and {ScreenCount} screens", result.Config.Frames.Count, result.Config.Screens.Count);
        return [];
    }
}
