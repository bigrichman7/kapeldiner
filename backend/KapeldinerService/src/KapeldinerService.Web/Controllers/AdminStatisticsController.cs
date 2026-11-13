using KapeldinerService.Domain.Events;
using KapeldinerService.Domain.Reservations;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Web.Controllers;

[ApiController]
[Route("api/admin/statistics")]
[Authorize(Policy = "AdministratorOnly")]
public sealed class AdminStatisticsController(
    IDbContextFactory<KapeldinerServiceDbContext> contextFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] Guid? eventId,
        CancellationToken cancellationToken)
    {
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        EventItem[] eventItems = await dbContext.EventItems
            .AsNoTracking()
            .OrderBy(item => item.StardAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        if (eventId.HasValue && eventItems.All(item => item.Id != new EventItemId(eventId.Value)))
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));

        int totalAllocatedQuotaSeats = await dbContext.EventQuotas
            .Select(quota => (int?)quota.AvailableSeats)
            .SumAsync(cancellationToken) ?? 0;

        var quotaQuery =
            from quota in dbContext.EventQuotas.AsNoTracking()
            join eventItem in dbContext.EventItems.AsNoTracking()
                on quota.EventItemId equals eventItem.Id
            join account in dbContext.AuthUsers.AsNoTracking()
                on quota.UserId equals account.Id into accounts
            from account in accounts.DefaultIfEmpty()
            where !eventId.HasValue || quota.EventItemId == new EventItemId(eventId.Value)
            select new QuotaRow(
                quota.UserId,
                account == null ? null : account.Name,
                quota.AvailableSeats,
                quota.UsedSeats);
        QuotaRow[] quotas = await quotaQuery.ToArrayAsync(cancellationToken);

        var ticketQuery = dbContext.Set<ReservationSeat>()
            .AsNoTracking()
            .Where(ticket => ticket.VerifiedAtUtc.HasValue
                && (!eventId.HasValue || ticket.EventItemId == new EventItemId(eventId.Value)))
            .Select(ticket => new VerifiedTicketRow(
                ticket.Reservation.UserId,
                ticket.EventItemId.Value,
                ticket.Reservation.EventItem.Title!,
                ticket.Seat.RowLabel,
                ticket.Seat.SeatNumber,
                ticket.VerifiedAtUtc!.Value));
        VerifiedTicketRow[] verifiedTickets = await ticketQuery.ToArrayAsync(cancellationToken);
        Dictionary<Guid, VerifiedTicketRow[]> ticketsByUser = verifiedTickets
            .GroupBy(ticket => ticket.UserId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        DepartmentStatisticsResponse[] departments = quotas
            .GroupBy(quota => quota.UserId)
            .Select(group =>
            {
                VerifiedTicketRow[] userTickets = ticketsByUser.GetValueOrDefault(group.Key) ?? [];
                return new DepartmentStatisticsResponse(
                    group.Key,
                    group.Select(quota => quota.Name).FirstOrDefault(name => name is not null),
                    group.Sum(quota => quota.AvailableSeats),
                    group.Sum(quota => quota.UsedSeats),
                    userTickets.Length,
                    userTickets
                        .OrderBy(ticket => ticket.EventTitle, StringComparer.Ordinal)
                        .ThenBy(ticket => ticket.RowLabel, StringComparer.Ordinal)
                        .ThenBy(ticket => ticket.SeatNumber)
                        .Select(ticket => new VerifiedTicketResponse(
                            ticket.EventId,
                            ticket.EventTitle,
                            ticket.RowLabel,
                            ticket.SeatNumber,
                            ticket.VerifiedAtUtc))
                        .ToArray());
            })
            .OrderByDescending(department => department.BookedSeats > 0
                ? (double)department.VerifiedSeats / department.BookedSeats
                : -1)
            .ThenByDescending(department => department.VerifiedSeats)
            .ThenBy(department => department.Name, StringComparer.Ordinal)
            .ToArray();

        StatisticsEventResponse[] events = eventItems
            .Select(item => new StatisticsEventResponse(
                item.Id.Value,
                item.Title!,
                item.StardAt,
                item.Status switch
                {
                    StatusEvent.Draft => "draft",
                    StatusEvent.Published => "published",
                    StatusEvent.Completed => "completed",
                    StatusEvent.Cancelled => "cancelled",
                    _ => throw new InvalidOperationException("The event status is not supported.")
                }))
            .ToArray();

        return Ok(ApiResponse.Ok(new AdminStatisticsResponse(
            eventId,
            events,
            totalAllocatedQuotaSeats,
            quotas.Sum(quota => quota.AvailableSeats),
            quotas.Sum(quota => quota.UsedSeats),
            verifiedTickets.Length,
            departments)));
    }

    private sealed record QuotaRow(
        Guid UserId,
        string? Name,
        int AvailableSeats,
        int UsedSeats);

    private sealed record VerifiedTicketRow(
        Guid UserId,
        Guid EventId,
        string EventTitle,
        string RowLabel,
        int SeatNumber,
        DateTime VerifiedAtUtc);

    public sealed record AdminStatisticsResponse(
        Guid? EventId,
        IReadOnlyList<StatisticsEventResponse> Events,
        int TotalAllocatedQuotaSeats,
        int AllocatedQuotaSeats,
        int BookedSeats,
        int VerifiedSeats,
        IReadOnlyList<DepartmentStatisticsResponse> Departments);

    public sealed record StatisticsEventResponse(
        Guid EventId,
        string Title,
        DateTime StartAt,
        string Status);

    public sealed record DepartmentStatisticsResponse(
        Guid UserId,
        string? Name,
        int AllocatedSeats,
        int BookedSeats,
        int VerifiedSeats,
        IReadOnlyList<VerifiedTicketResponse> Tickets);

    public sealed record VerifiedTicketResponse(
        Guid EventId,
        string EventTitle,
        string RowLabel,
        int SeatNumber,
        DateTime VerifiedAtUtc);
}
