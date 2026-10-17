using System.Data.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Web.Events;

public sealed partial class EventCompletionBackgroundService(
    EventCompletionService completionService,
    TimeProvider timeProvider,
    ILogger<EventCompletionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await completionService.CompleteEndedEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (DbException exception)
            {
                LogCompletionFailed(logger, exception);
            }
            catch (DbUpdateException exception)
            {
                LogCompletionFailed(logger, exception);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to complete ended events.")]
    private static partial void LogCompletionFailed(ILogger logger, Exception exception);
}
