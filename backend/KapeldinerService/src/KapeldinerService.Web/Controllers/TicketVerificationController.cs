using KapeldinerService.Domain.Events;
using KapeldinerService.Domain.Reservations;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Web.Controllers;

[ApiController]
[Route("api/tickets")]
public sealed class TicketVerificationController(
    IDbContextFactory<KapeldinerServiceDbContext> contextFactory,
    TimeProvider timeProvider) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("verify/{token}")]
    public async Task<IActionResult> Verify(string token, CancellationToken cancellationToken)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";

        if (token.Length is < 32 or > 43)
            return Ok(ApiResponse.Ok(TicketVerificationResponse.NotFound));

        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        ReservationSeat? ticket = await dbContext.Set<ReservationSeat>()
            .AsNoTracking()
            .Include(seat => seat.Reservation)
            .ThenInclude(reservation => reservation.EventItem)
            .SingleOrDefaultAsync(seat => seat.VerificationToken == token, cancellationToken);

        if (ticket is null || ticket.Reservation.EventItem.Status is StatusEvent.Draft or StatusEvent.Cancelled)
            return Ok(ApiResponse.Ok(TicketVerificationResponse.NotFound));

        EventItem eventItem = ticket.Reservation.EventItem;
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        string status;
        if (ticket.VerifiedAtUtc.HasValue)
        {
            status = "alreadyVerified";
        }
        else if (nowUtc < eventItem.StardAt.AddHours(-5))
        {
            status = "tooEarly";
        }
        else
        {
            int updatedRows = await dbContext.Set<ReservationSeat>()
                .Where(seat => seat.Id == ticket.Id && seat.VerifiedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(seat => seat.VerifiedAtUtc, (DateTime?)nowUtc),
                    cancellationToken);
            status = updatedRows == 1 ? "verified" : "alreadyVerified";
        }

        return Ok(ApiResponse.Ok(new TicketVerificationResponse(
            status,
            eventItem.Title!,
            eventItem.StardAt,
            ticket.Seat.RowLabel,
            ticket.Seat.SeatNumber)));
    }

    public sealed record TicketVerificationResponse(
        string Status,
        string? EventTitle,
        DateTime? StartAt,
        string? RowLabel,
        int? SeatNumber)
    {
        public static TicketVerificationResponse NotFound { get; } =
            new("notFound", null, null, null, null);
    }
}
