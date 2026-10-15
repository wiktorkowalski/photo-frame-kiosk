namespace FrameRouter.Screens;

internal sealed class ScreenTicker(ScreenState screenState, TimeProvider timeProvider, ILogger<ScreenTicker> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval, timeProvider);
        do
        {
            try
            {
                screenState.Refresh();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The schedule check failed; frames keep their current screen");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
