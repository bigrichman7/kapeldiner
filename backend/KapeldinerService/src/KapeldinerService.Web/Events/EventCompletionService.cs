using KapeldinerService.Domain.Events;
using KapeldinerService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Web.Events;

public sealed class EventCompletionService(
    IDbContextFactory<KapeldinerServiceDbContext> contextFactory,
    TimeProvider timeProvider)
{
    public async Task<int> CompleteEndedEventsAsync(CancellationToken cancellationToken)
    {
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.EventItems
            .Where(eventItem =>
                eventItem.Status != StatusEvent.Completed
                && eventItem.Status != StatusEvent.Cancelled
                && eventItem.EndAt <= nowUtc)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(eventItem => eventItem.Status, StatusEvent.Completed),
                cancellationToken);
    }
}
