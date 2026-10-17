using System.Globalization;
using KapeldinerService.Domain.Events;
using KapeldinerService.Domain.Reservations;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Web.Auth;
using KapeldinerService.Web.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KapeldinerService.Web.Controllers;

[ApiController]
[Route("api/admin/events/{eventId:guid}/quotas")]
[Authorize(Policy = "AdministratorOnly")]
public sealed class EventQuotasController(
    IDbContextFactory<KapeldinerServiceDbContext> contextFactory,
    EventCompletionService completionService,
    TimeProvider timeProvider) : ControllerBase
{
    private const int MaximumQuotaSeats = 643;

    [HttpGet]
    public async Task<IActionResult> GetQuotas(Guid eventId, CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await dbContext.EventItems.AnyAsync(item => item.Id == new EventItemId(eventId), cancellationToken))
        {
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));
        }

        var quotaRows = await (
            from quota in dbContext.EventQuotas.AsNoTracking()
            join account in dbContext.AuthUsers.AsNoTracking() on quota.UserId equals account.Id into accounts
            from user in accounts.DefaultIfEmpty()
            where quota.EventItemId == new EventItemId(eventId)
            orderby quota.UserId
            select new { Quota = quota, User = user })
            .ToArrayAsync(cancellationToken);
        QuotaResponse[] quotas = quotaRows.Select(row => ToResponse(row.Quota, row.User)).ToArray();
        return Ok(ApiResponse.Ok(quotas));
    }

    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> GetQuota(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await dbContext.EventItems.AnyAsync(item => item.Id == new EventItemId(eventId), cancellationToken))
        {
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));
        }

        var quotaRow = await (
            from eventQuota in dbContext.EventQuotas.AsNoTracking()
            join account in dbContext.AuthUsers.AsNoTracking() on eventQuota.UserId equals account.Id into accounts
            from user in accounts.DefaultIfEmpty()
            where eventQuota.EventItemId == new EventItemId(eventId)
                && eventQuota.UserId == userId
            select new { Quota = eventQuota, User = user })
            .SingleOrDefaultAsync(cancellationToken);
        return quotaRow is null
            ? NotFound(ApiResponse.Failure<object>("QUOTA_NOT_FOUND", "Квота пользователя не найдена."))
            : Ok(ApiResponse.Ok(ToResponse(quotaRow.Quota, quotaRow.User)));
    }

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> UpsertQuota(
        Guid eventId,
        Guid userId,
        [FromBody] UpdateQuotaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out int availableSeats, out string[] cancelSeatIds,
                out HashSet<string> seatsToCancel,
                out Dictionary<string, string[]> fields))
        {
            return ValidationFailure(fields);
        }

        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        EventItem? eventItem = await dbContext.EventItems
            .SingleOrDefaultAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        IActionResult? eventError = ValidateEditableEvent(eventItem);
        if (eventError is not null)
            return eventError;

        bool userExists = await dbContext.AuthUsers.AnyAsync(
            user => user.Id == userId && user.Role == AuthRoles.User,
            cancellationToken);
        if (!userExists)
        {
            return NotFound(ApiResponse.Failure<object>("USER_NOT_FOUND", "Пользователь не найден."));
        }

        EventQuota? quota = await dbContext.EventQuotas
            .Include(item => item.Reservation)
            .ThenInclude(reservation => reservation!.ReservationSeats)
            .SingleOrDefaultAsync(
                item => item.EventItemId == new EventItemId(eventId) && item.UserId == userId,
                cancellationToken);

        if (quota is null)
        {
            if (cancelSeatIds.Length > 0)
            {
                return ValidationFailure(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["cancelSeatIds"] = ["Нельзя отменить бронирования для еще не назначенной квоты."]
                });
            }

            var createResult = EventQuota.Create(new EventItemId(eventId), userId, availableSeats);
            if (createResult.IsFailure)
                return DomainFailure(createResult.Error);

            quota = createResult.Value;
            dbContext.EventQuotas.Add(quota);
        }
        else
        {
            int actualUsedSeats = quota.Reservation?.ReservationSeats.Count ?? 0;
            if (quota.UsedSeats != actualUsedSeats)
            {
                return Conflict(ApiResponse.Failure<object>(
                    "QUOTA_USAGE_INCONSISTENT",
                    "Использование квоты не совпадает с числом забронированных мест."));
            }

            int requiredCancellations = Math.Max(0, quota.UsedSeats - availableSeats);
            if (cancelSeatIds.Length != requiredCancellations)
            {
                string message = requiredCancellations > 0
                    ? "Для уменьшения квоты ниже использованного количества укажите места, которые нужно отменить."
                    : "Отменять бронирования можно только при уменьшении квоты ниже использованного количества.";
                return ValidationFailure(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["cancelSeatIds"] = [message]
                });
            }

            if (requiredCancellations > 0)
            {
                HashSet<string> existingSeats = quota.Reservation!.ReservationSeats
                    .Select(seat => seat.SeatId)
                    .ToHashSet(StringComparer.Ordinal);
                if (!seatsToCancel.IsSubsetOf(existingSeats))
                {
                    return ValidationFailure(new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["cancelSeatIds"] = ["Укажите только места, забронированные этим пользователем."]
                    });
                }

                int removedSeats = quota.Reservation.RemoveSeatIds(seatsToCancel);
                if (removedSeats != requiredCancellations)
                {
                    return Conflict(ApiResponse.Failure<object>(
                        "QUOTA_USAGE_INCONSISTENT",
                        "Не удалось согласовать отменяемые места с использованием квоты."));
                }

                var cancellationResult = quota.CancelSeats(removedSeats);
                if (cancellationResult.IsFailure)
                    return DomainFailure(cancellationResult.Error);

                if (quota.Reservation.ReservationSeats.Count == 0)
                    dbContext.Remove(quota.Reservation);
            }

            var updateResult = quota.UpdateAvailableSeats(availableSeats);
            if (updateResult.IsFailure)
                return DomainFailure(updateResult.Error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (
            FindPostgresException(exception) is
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_event_quota_event_user"
            })
        {
            return Conflict(ApiResponse.Failure<object>(
                "QUOTA_ALREADY_EXISTS",
                "Для этого пользователя уже назначена квота на мероприятие."));
        }
        catch (Exception exception) when (
            FindPostgresException(exception) is { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            return Conflict(ApiResponse.Failure<object>(
                "QUOTA_UPDATE_CONFLICT",
                "Квота была изменена параллельно. Обновите данные и повторите запрос."));
        }

        AuthUser user = await dbContext.AuthUsers.SingleAsync(account => account.Id == userId, cancellationToken);
        return Ok(ApiResponse.Ok(ToResponse(quota, user)));
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> DeleteQuota(Guid eventId, Guid userId, CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        EventItem? eventItem = await dbContext.EventItems
            .SingleOrDefaultAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        IActionResult? eventError = ValidateEditableEvent(eventItem);
        if (eventError is not null)
            return eventError;

        EventQuota? quota = await dbContext.EventQuotas
            .Include(item => item.Reservation)
            .ThenInclude(reservation => reservation!.ReservationSeats)
            .SingleOrDefaultAsync(
                item => item.EventItemId == new EventItemId(eventId) && item.UserId == userId,
                cancellationToken);
        if (quota is null)
        {
            return NotFound(ApiResponse.Failure<object>("QUOTA_NOT_FOUND", "Квота пользователя не найдена."));
        }

        if (quota.UsedSeats != 0 || (quota.Reservation?.ReservationSeats.Count ?? 0) > 0)
        {
            return Conflict(ApiResponse.Failure<object>(
                "QUOTA_HAS_RESERVATIONS",
                "Нельзя удалить квоту, пока у пользователя есть забронированные места."));
        }

        dbContext.EventQuotas.Remove(quota);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ApiResponse.Ok(new { deleted = true }));
    }

    private IActionResult? ValidateEditableEvent(EventItem? eventItem)
    {
        if (eventItem is null)
        {
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));
        }

        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (eventItem.Status is StatusEvent.Completed or StatusEvent.Cancelled
            || eventItem.StardAt <= nowUtc
            || eventItem.EndAt <= nowUtc)
        {
            return Conflict(ApiResponse.Failure<object>(
                "EVENT_NOT_EDITABLE",
                "Квоты нельзя изменить после начала мероприятия или после его завершения."));
        }

        return null;
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
                return postgresException;
        }

        return null;
    }

    private static bool TryNormalize(
        UpdateQuotaRequest request,
        out int availableSeats,
        out string[] cancelSeatIds,
        out HashSet<string> seatsToCancel,
        out Dictionary<string, string[]> fields)
    {
        availableSeats = request.AvailableSeats ?? 0;
        cancelSeatIds = request.CancelSeatIds?.ToArray() ?? [];
        seatsToCancel = [];
        fields = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!request.AvailableSeats.HasValue || availableSeats < 0 || availableSeats > MaximumQuotaSeats)
        {
            fields["availableSeats"] = [$"Квота должна быть от 0 до {MaximumQuotaSeats} мест."];
        }

        foreach (string seatId in cancelSeatIds)
        {
            if (!TryParseSeatId(seatId))
            {
                fields["cancelSeatIds"] = ["Укажите места в формате «ряд-место», например «1-2»."];
                break;
            }

            if (!seatsToCancel.Add(seatId))
            {
                fields["cancelSeatIds"] = ["В списке нельзя указывать одно место несколько раз."];
                break;
            }
        }

        return fields.Count == 0;
    }

    private static bool TryParseSeatId(string? seatId)
    {
        string[] parts = seatId?.Split('-', StringSplitOptions.None) ?? [];
        if (parts.Length != 2
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seatNumber)
            || seatNumber < 1)
        {
            return false;
        }

        return Seat.Create(parts[0], seatNumber, SeatType.booked).IsSuccess;
    }

    private BadRequestObjectResult ValidationFailure(IReadOnlyDictionary<string, string[]> fields) =>
        BadRequest(new ApiEnvelope<object>(false, null,
            new ApiError("VALIDATION_FAILED", "Проверьте введенные данные.", fields)));

    private IActionResult DomainFailure(KapeldinerService.Domain.Common.Errors.DomainError error)
    {
        if (error.Code.StartsWith("GeneralErrors.", StringComparison.Ordinal))
        {
            return BadRequest(ApiResponse.Failure<object>("VALIDATION_FAILED", error.Message));
        }

        return Conflict(ApiResponse.Failure<object>(error.Code, error.Message));
    }

    private static QuotaResponse ToResponse(EventQuota quota, AuthUser? user) =>
        new(
            quota.EventItemId.Value,
            quota.UserId,
            user?.Login,
            user?.Name,
            quota.AvailableSeats,
            quota.UsedSeats,
            quota.AvailableSeats - quota.UsedSeats);

    public sealed record UpdateQuotaRequest(int? AvailableSeats, IReadOnlyList<string>? CancelSeatIds);

    public sealed record QuotaResponse(
        Guid EventId,
        Guid UserId,
        string? Login,
        string? Name,
        int AvailableSeats,
        int UsedSeats,
        int RemainingSeats);
}
