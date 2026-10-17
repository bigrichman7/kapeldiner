using System.Security.Claims;
using System.Data;
using KapeldinerService.Domain.Events;
using KapeldinerService.Domain.Reservations;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Web.Auth;
using KapeldinerService.Web.Events;
using KapeldinerService.Web.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KapeldinerService.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class ReservationsController(
    IDbContextFactory<KapeldinerServiceDbContext> contextFactory,
    EventCompletionService completionService,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("events/{eventId:guid}/my-reservation")]
    [Authorize(Roles = AuthRoles.User)]
    public async Task<IActionResult> GetMyReservation(Guid eventId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
            return Unauthorized(ApiResponse.Failure<object>("UNAUTHENTICATED", "Требуется повторный вход."));

        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem? eventItem = await dbContext.EventItems
            .AsNoTracking()
            .Where(item => item.Id == new EventItemId(eventId) && item.Status == StatusEvent.Published)
            .SingleOrDefaultAsync(cancellationToken);
        if (eventItem is null)
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));

        EventQuota? quota = await dbContext.EventQuotas
            .AsNoTracking()
            .Include(item => item.Reservation)
            .ThenInclude(reservation => reservation!.ReservationSeats)
            .SingleOrDefaultAsync(
                item => item.EventItemId == eventItem.Id && item.UserId == userId && item.AvailableSeats > 0,
                cancellationToken);
        if (quota is null)
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));

        return Ok(ApiResponse.Ok(ToResponse(quota)));
    }

    [HttpPut("events/{eventId:guid}/my-reservation")]
    [Authorize(Roles = AuthRoles.User)]
    public async Task<IActionResult> ReplaceMyReservation(
        Guid eventId,
        [FromBody] ReplaceReservationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
            return Unauthorized(ApiResponse.Failure<object>("UNAUTHENTICATED", "Требуется повторный вход."));

        if (!TryNormalize(request, out string[] seatIds, out Dictionary<string, string[]> fields))
            return ValidationFailure(fields);

        return await ReplaceReservationAsync(eventId, userId, seatIds, administrator: false, cancellationToken);
    }

    [HttpGet("events/{eventId:guid}/seats")]
    [Authorize(Roles = AuthRoles.User)]
    public async Task<IActionResult> GetMyEventSeats(Guid eventId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
            return Unauthorized(ApiResponse.Failure<object>("UNAUTHENTICATED", "Требуется повторный вход."));

        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem? eventItem = await dbContext.EventItems
            .AsNoTracking()
            .Where(item => item.Id == new EventItemId(eventId) && item.Status == StatusEvent.Published)
            .SingleOrDefaultAsync(cancellationToken);
        bool hasQuota = eventItem is not null && await dbContext.EventQuotas
            .AnyAsync(quota => quota.EventItemId == eventItem.Id
                && quota.UserId == userId
                && quota.AvailableSeats > 0, cancellationToken);
        if (eventItem is null || !hasQuota)
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));

        HashSet<string> bookedSeatIds = await dbContext.Set<ReservationSeat>()
            .AsNoTracking()
            .Where(seat => seat.EventItemId == eventItem.Id)
            .Select(seat => seat.SeatKey)
            .ToHashSetAsync(cancellationToken);
        HashSet<string> designatedSeatIds = await dbContext.EventSeatDesignations
            .AsNoTracking()
            .Where(seat => seat.EventItemId == eventItem.Id)
            .Select(seat => seat.RowLabel + "-" + seat.SeatNumber)
            .ToHashSetAsync(cancellationToken);

        return Ok(ApiResponse.Ok(HallSeatCatalog.GetAll(eventItem.EnableAdditionalRow12A)
            .Select(seat => new UserSeatResponse(
                seat.SeatId,
                !bookedSeatIds.Contains(seat.SeatId) && !designatedSeatIds.Contains(seat.SeatId)))
            .ToArray()));
    }

    [HttpGet("admin/events/{eventId:guid}/seats")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> GetAdminEventSeats(Guid eventId, CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem? eventItem = await dbContext.EventItems
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        if (eventItem is null)
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));

        var bookings = await (
            from seat in dbContext.Set<ReservationSeat>().AsNoTracking()
            join reservation in dbContext.Set<Reservation>().AsNoTracking()
                on seat.ReservationId equals reservation.Id
            join user in dbContext.AuthUsers.AsNoTracking()
                on reservation.UserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            where seat.EventItemId == eventItem.Id
            select new { seat.SeatKey, reservation.UserId, UserName = user == null ? null : user.Name })
            .ToDictionaryAsync(seat => seat.SeatKey, cancellationToken);
        Dictionary<string, string> designations = await dbContext.EventSeatDesignations
            .AsNoTracking()
            .Where(seat => seat.EventItemId == eventItem.Id)
            .ToDictionaryAsync(
                seat => seat.RowLabel + "-" + seat.SeatNumber,
                seat => seat.Designation == SeatDesignation.Vip ? "vip" : "organization",
                cancellationToken);

        return Ok(ApiResponse.Ok(HallSeatCatalog.GetAll(eventItem.EnableAdditionalRow12A)
            .Select(seat =>
            {
                bookings.TryGetValue(seat.SeatId, out var booking);
                designations.TryGetValue(seat.SeatId, out string? designation);
                return new AdminSeatResponse(
                    seat.SeatId,
                    designation,
                    booking is not null,
                    booking?.UserId,
                    booking?.UserName);
            })
            .ToArray()));
    }

    [HttpPut("admin/events/{eventId:guid}/seats/{seatId}/designation")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> SetSeatDesignation(
        Guid eventId,
        string seatId,
        [FromBody] SetSeatDesignationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Designation is not (null or "none" or "vip" or "organization"))
        {
            return ValidationFailure(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["designation"] = ["Допустимые значения: vip, organization или none."]
            });
        }

        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        EventItem? eventItem = await dbContext.EventItems
            .SingleOrDefaultAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        IActionResult? eventError = ValidateAdminEditableEvent(eventItem);
        if (eventError is not null)
            return eventError;
        if (!HallSeatCatalog.TryCreateSeat(seatId, eventItem!.EnableAdditionalRow12A, out Seat? seat))
        {
            return ValidationFailure(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["seatId"] = ["Укажите существующее место, включенное в конфигурацию мероприятия."]
            });
        }

        EventSeatDesignation? existing = await dbContext.EventSeatDesignations
            .SingleOrDefaultAsync(
                item => item.EventItemId == eventItem.Id
                    && item.RowLabel == seat!.RowLabel
                    && item.SeatNumber == seat.SeatNumber,
                cancellationToken);
        bool isBooked = await dbContext.Set<ReservationSeat>()
            .AnyAsync(item => item.EventItemId == eventItem.Id && item.SeatKey == seat!.SeatId, cancellationToken);
        if (isBooked && request.Designation is not null and not "none")
        {
            return Conflict(ApiResponse.Failure<object>(
                "SEAT_ALREADY_BOOKED",
                "Нельзя назначить категорию уже забронированному месту."));
        }

        if (request.Designation is null or "none")
        {
            if (existing is not null)
                dbContext.EventSeatDesignations.Remove(existing);
        }
        else
        {
            SeatDesignation value = string.Equals(request.Designation, "vip", StringComparison.Ordinal)
                ? SeatDesignation.Vip
                : SeatDesignation.Organization;
            if (existing is null)
                dbContext.EventSeatDesignations.Add(new EventSeatDesignation(eventItem.Id, seat!.RowLabel, seat.SeatNumber, value));
            else
                dbContext.Entry(existing).Property(item => item.Designation).CurrentValue = value;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            FindPostgresException(exception) is { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            return Conflict(ApiResponse.Failure<object>(
                "SEAT_UPDATE_CONFLICT",
                "Состояние места изменилось параллельно. Обновите схему зала."));
        }

        eventError = ValidateAdminEditableEvent(eventItem);
        if (eventError is not null)
            return eventError;
        await transaction.CommitAsync(cancellationToken);
        return Ok(ApiResponse.Ok(new { seatId, designation = request.Designation is "none" ? null : request.Designation }));
    }

    [HttpDelete("events/{eventId:guid}/my-reservation")]
    [Authorize(Roles = AuthRoles.User)]
    public async Task<IActionResult> CancelMyReservation(Guid eventId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
            return Unauthorized(ApiResponse.Failure<object>("UNAUTHENTICATED", "Требуется повторный вход."));

        return await ReplaceReservationAsync(eventId, userId, [], administrator: false, cancellationToken);
    }

    [HttpGet("admin/events/{eventId:guid}/reservations")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> GetEventReservations(Guid eventId, CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        bool eventExists = await dbContext.EventItems
            .AnyAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        if (!eventExists)
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));

        Reservation[] reservations = await dbContext.Set<Reservation>()
            .AsNoTracking()
            .Where(reservation => reservation.EventItemId == new EventItemId(eventId))
            .Include(reservation => reservation.EventQuota)
            .Include(reservation => reservation.ReservationSeats)
            .OrderBy(reservation => reservation.UserId)
            .ToArrayAsync(cancellationToken);
        return Ok(ApiResponse.Ok(reservations.Select(ToResponse)));
    }

    [HttpGet("admin/events/{eventId:guid}/statistics")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> GetEventStatistics(Guid eventId, CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem? eventItem = await dbContext.EventItems
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        if (eventItem is null)
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));

        int totalSeats = HallSeatCatalog.GetAll(eventItem.EnableAdditionalRow12A).Count;
        EventQuota[] quotas = await dbContext.EventQuotas
            .AsNoTracking()
            .Where(quota => quota.EventItemId == eventItem.Id)
            .OrderBy(quota => quota.UserId)
            .ToArrayAsync(cancellationToken);
        int reservedSeats = quotas.Sum(quota => quota.UsedSeats);
        int unavailableSeats = await dbContext.EventSeatDesignations
            .CountAsync(seat => seat.EventItemId == eventItem.Id, cancellationToken);
        var userRows = await (
            from quota in dbContext.EventQuotas.AsNoTracking()
            join user in dbContext.AuthUsers.AsNoTracking() on quota.UserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            where quota.EventItemId == eventItem.Id
            orderby quota.UserId
            select new
            {
                quota.UserId,
                Name = user == null ? null : user.Name,
                quota.AvailableSeats,
                quota.UsedSeats
            })
            .ToArrayAsync(cancellationToken);

        return Ok(ApiResponse.Ok(new EventStatisticsResponse(
            eventId,
            totalSeats,
            reservedSeats,
            totalSeats - reservedSeats,
            totalSeats - reservedSeats - unavailableSeats,
            userRows.Select(row => new UserQuotaStatistics(
                row.UserId,
                row.Name,
                row.AvailableSeats,
                row.UsedSeats,
                row.AvailableSeats - row.UsedSeats)).ToArray())));
    }

    [HttpGet("admin/events/{eventId:guid}/quotas/{userId:guid}/reservation")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> GetUserReservation(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventQuota? quota = await dbContext.EventQuotas
            .AsNoTracking()
            .Where(item => item.EventItemId == new EventItemId(eventId) && item.UserId == userId)
            .Include(item => item.Reservation)
            .ThenInclude(reservation => reservation!.ReservationSeats)
            .SingleOrDefaultAsync(cancellationToken);
        return quota is null
            ? NotFound(ApiResponse.Failure<object>("QUOTA_NOT_FOUND", "Квота пользователя не найдена."))
            : Ok(ApiResponse.Ok(ToResponse(quota)));
    }

    [HttpPut("admin/events/{eventId:guid}/quotas/{userId:guid}/reservation")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> ReplaceUserReservation(
        Guid eventId,
        Guid userId,
        [FromBody] ReplaceReservationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out string[] seatIds, out Dictionary<string, string[]> fields))
            return ValidationFailure(fields);

        return await ReplaceReservationAsync(eventId, userId, seatIds, administrator: true, cancellationToken);
    }

    [HttpDelete("admin/events/{eventId:guid}/quotas/{userId:guid}/reservation")]
    [Authorize(Policy = "AdministratorOnly")]
    public Task<IActionResult> CancelUserReservation(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken) =>
        ReplaceReservationAsync(eventId, userId, [], administrator: true, cancellationToken);

    private async Task<IActionResult> ReplaceReservationAsync(
        Guid eventId,
        Guid userId,
        string[] seatIds,
        bool administrator,
        CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        EventItem? eventItem = await dbContext.EventItems
            .SingleOrDefaultAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        IActionResult? eventError = ValidateBookableEvent(eventItem);
        if (eventError is not null)
            return eventError;

        EventQuota? quota = await dbContext.EventQuotas
            .Include(item => item.Reservation)
            .ThenInclude(reservation => reservation!.ReservationSeats)
            .SingleOrDefaultAsync(
                item => item.EventItemId == new EventItemId(eventId) && item.UserId == userId,
                cancellationToken);
        if (quota is null || quota.AvailableSeats <= 0)
        {
            return administrator
                ? NotFound(ApiResponse.Failure<object>("QUOTA_NOT_FOUND", "Квота пользователя не найдена."))
                : NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));
        }

        if (administrator && !await dbContext.AuthUsers
                .AnyAsync(user => user.Id == userId && user.Role == AuthRoles.User, cancellationToken))
        {
            return NotFound(ApiResponse.Failure<object>("USER_NOT_FOUND", "Пользователь не найден."));
        }

        List<Seat> requestedSeats = [];
        foreach (string seatId in seatIds)
        {
            if (!HallSeatCatalog.TryCreateSeat(seatId, eventItem!.EnableAdditionalRow12A, out Seat? seat))
            {
                return ValidationFailure(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["seatIds"] = ["Укажите существующие места, включенные в конфигурацию мероприятия."]
                });
            }
            requestedSeats.Add(seat!);
        }

        Reservation? reservation = quota.Reservation;
        int oldSeatCount = reservation?.ReservationSeats.Count ?? 0;
        if (quota.UsedSeats != oldSeatCount)
        {
            return Conflict(ApiResponse.Failure<object>(
                "QUOTA_USAGE_INCONSISTENT",
                "Использование квоты не совпадает с числом забронированных мест."));
        }

        HashSet<string> existingSeatIds = reservation?.ReservationSeats
            .Select(reservationSeat => reservationSeat.SeatId)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        HashSet<string> requestedSeatIds = requestedSeats
            .Select(seat => seat.SeatId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> addedSeatIds = requestedSeatIds
            .Except(existingSeatIds, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        if (addedSeatIds.Count > 0)
        {
            HashSet<string> designatedSeatIds = await dbContext.EventSeatDesignations
                .AsNoTracking()
                .Where(seat => seat.EventItemId == eventItem!.Id)
                .Select(seat => seat.RowLabel + "-" + seat.SeatNumber)
                .ToHashSetAsync(cancellationToken);
            if (!administrator && addedSeatIds.Any(designatedSeatIds.Contains))
            {
                return Conflict(ApiResponse.Failure<object>(
                    "SEAT_UNAVAILABLE",
                    "Одно или несколько выбранных мест недоступны."));
            }

            string[] addedSeatIdArray = addedSeatIds.ToArray();
            bool anyAddedSeatOccupied = await dbContext.Set<ReservationSeat>()
                .AsNoTracking()
                .AnyAsync(seat => seat.EventItemId == eventItem!.Id
                    && addedSeatIdArray.Contains(seat.SeatKey), cancellationToken);
            if (anyAddedSeatOccupied)
            {
                return Conflict(ApiResponse.Failure<object>(
                    "SEAT_ALREADY_BOOKED",
                    "Одно или несколько мест уже заняты. Обновите схему зала."));
            }
        }

        if (requestedSeats.Count > quota.AvailableSeats)
        {
            return Conflict(ApiResponse.Failure<object>(
                "QUOTA_EXCEEDED",
                "Выбранное количество мест превышает выделенную квоту."));
        }

        Reservation? newReservation = null;
        if (requestedSeats.Count > 0 && reservation is null)
        {
            var createResult = Reservation.Create(eventItem!.Id, userId, requestedSeats, quota);
            if (createResult.IsFailure)
                return DomainFailure(createResult.Error);
            newReservation = createResult.Value;
        }

        int seatCountDifference = requestedSeats.Count - oldSeatCount;
        if (seatCountDifference > 0)
        {
            var reserveResult = quota.ReserveSeats(seatCountDifference);
            if (reserveResult.IsFailure)
                return DomainFailure(reserveResult.Error);
        }
        else if (seatCountDifference < 0)
        {
            var cancelResult = quota.CancelSeats(-seatCountDifference);
            if (cancelResult.IsFailure)
                return DomainFailure(cancelResult.Error);
        }

        if (requestedSeats.Count == 0)
        {
            if (reservation is not null)
                dbContext.Remove(reservation);
        }
        else if (newReservation is not null)
        {
            reservation = newReservation;
            dbContext.Add(reservation);
        }
        else
        {
            reservation!.ReplaceSeats(requestedSeats);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            FindPostgresException(exception) is
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_reservation_seat_event_seat"
            })
        {
            return Conflict(ApiResponse.Failure<object>(
                "SEAT_ALREADY_BOOKED",
                "Одно или несколько мест уже заняты. Обновите схему зала."));
        }
        catch (Exception exception) when (
            FindPostgresException(exception) is { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            return Conflict(ApiResponse.Failure<object>(
                "RESERVATION_CONFLICT",
                "Бронирование изменилось параллельно. Обновите данные и повторите запрос."));
        }

        eventError = ValidateBookableEvent(eventItem);
        if (eventError is not null)
            return eventError;

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (
            FindPostgresException(exception) is { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            return Conflict(ApiResponse.Failure<object>(
                "RESERVATION_CONFLICT",
                "Бронирование изменилось параллельно. Обновите данные и повторите запрос."));
        }

        ReservationResponse response = new(
            reservation?.Id.Value,
            eventId,
            userId,
            requestedSeats.Select(seat => seat.SeatId).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            quota.AvailableSeats,
            quota.UsedSeats,
            quota.AvailableSeats - quota.UsedSeats);
        return Ok(ApiResponse.Ok(response));
    }

    private IActionResult? ValidateBookableEvent(EventItem? eventItem)
    {
        if (eventItem is null)
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));

        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (eventItem.Status != StatusEvent.Published
            || eventItem.StardAt <= nowUtc
            || eventItem.EndAt <= nowUtc)
        {
            return Conflict(ApiResponse.Failure<object>(
                "EVENT_NOT_BOOKABLE",
                "Бронирование доступно только для опубликованных мероприятий до их начала."));
        }

        return null;
    }

    private IActionResult? ValidateAdminEditableEvent(EventItem? eventItem)
    {
        if (eventItem is null)
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));

        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (eventItem.Status is StatusEvent.Completed or StatusEvent.Cancelled
            || eventItem.StardAt <= nowUtc
            || eventItem.EndAt <= nowUtc)
        {
            return Conflict(ApiResponse.Failure<object>(
                "EVENT_NOT_EDITABLE",
                "Обозначения мест нельзя изменить после начала мероприятия или после его завершения."));
        }

        return null;
    }

    private BadRequestObjectResult ValidationFailure(IReadOnlyDictionary<string, string[]> fields) =>
        BadRequest(new ApiEnvelope<object>(false, null,
            new ApiError("VALIDATION_FAILED", "Проверьте введенные данные.", fields)));

    private BadRequestObjectResult DomainFailure(KapeldinerService.Domain.Common.Errors.DomainError error) =>
        BadRequest(ApiResponse.Failure<object>("VALIDATION_FAILED", error.Message));

    private bool TryGetCurrentUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

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
        ReplaceReservationRequest? request,
        out string[] seatIds,
        out Dictionary<string, string[]> fields)
    {
        seatIds = request?.SeatIds?.ToArray() ?? [];
        fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request?.SeatIds is null)
        {
            fields["seatIds"] = ["Укажите список выбранных мест."];
        }
        else if (seatIds.Any(string.IsNullOrWhiteSpace))
        {
            fields["seatIds"] = ["Идентификаторы мест не могут быть пустыми."];
        }
        else if (seatIds.Distinct(StringComparer.Ordinal).Count() != seatIds.Length)
        {
            fields["seatIds"] = ["В списке нельзя указывать одно место несколько раз."];
        }

        return fields.Count == 0;
    }

    private static ReservationResponse ToResponse(Reservation reservation) =>
        new(
            reservation.Id.Value,
            reservation.EventItemId.Value,
            reservation.UserId,
            reservation.ReservationSeats
                .Select(seat => seat.SeatId)
                .OrderBy(seatId => seatId, StringComparer.Ordinal)
                .ToArray(),
            reservation.EventQuota.AvailableSeats,
            reservation.EventQuota.UsedSeats,
            reservation.EventQuota.AvailableSeats - reservation.EventQuota.UsedSeats);

    private static ReservationResponse ToResponse(EventQuota quota) =>
        quota.Reservation is null
            ? new ReservationResponse(
                null,
                quota.EventItemId.Value,
                quota.UserId,
                [],
                quota.AvailableSeats,
                quota.UsedSeats,
                quota.AvailableSeats - quota.UsedSeats)
            : ToResponse(quota.Reservation);

    public sealed record ReplaceReservationRequest(IReadOnlyList<string>? SeatIds);

    public sealed record SetSeatDesignationRequest(string? Designation);

    public sealed record UserSeatResponse(string SeatId, bool Available);

    public sealed record AdminSeatResponse(
        string SeatId,
        string? Designation,
        bool Booked,
        Guid? UserId,
        string? UserName);

    public sealed record EventStatisticsResponse(
        Guid EventId,
        int TotalSeats,
        int ReservedSeats,
        int UnreservedSeats,
        int UserBookableSeats,
        IReadOnlyList<UserQuotaStatistics> Users);

    public sealed record UserQuotaStatistics(
        Guid UserId,
        string? Name,
        int AvailableSeats,
        int UsedSeats,
        int RemainingSeats);

    public sealed record ReservationResponse(
        Guid? ReservationId,
        Guid EventId,
        Guid UserId,
        IReadOnlyList<string> SeatIds,
        int AvailableSeats,
        int UsedSeats,
        int RemainingSeats);
}
