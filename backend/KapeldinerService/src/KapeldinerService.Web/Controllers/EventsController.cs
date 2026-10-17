using System.Security.Claims;
using KapeldinerService.Domain.Common.Errors;
using KapeldinerService.Domain.Events;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Web.Auth;
using KapeldinerService.Web.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class EventsController(
    IDbContextFactory<KapeldinerServiceDbContext> contextFactory,
    EventCompletionService completionService,
    TimeProvider timeProvider) : ControllerBase
{
    private static readonly TimeZoneInfo EventTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    [HttpGet("admin/events")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> GetAdminEvents(CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem[] events = await dbContext.EventItems
            .AsNoTracking()
            .Include(eventItem => eventItem.EventQuotas)
            .OrderBy(eventItem => eventItem.StardAt)
            .ToArrayAsync(cancellationToken);
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        return Ok(ApiResponse.Ok(events.Select(eventItem => ToResponse(eventItem, nowUtc, includeQuotas: true))));
    }

    [HttpGet("admin/events/{eventId:guid}")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> GetAdminEvent(Guid eventId, CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem? eventItem = await dbContext.EventItems
            .AsNoTracking()
            .Include(item => item.EventQuotas)
            .SingleOrDefaultAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        return eventItem is null
            ? NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."))
            : Ok(ApiResponse.Ok(ToResponse(eventItem, timeProvider.GetUtcNow().UtcDateTime, includeQuotas: true)));
    }

    [HttpPost("admin/events")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> CreateEvent(
        [FromBody] CreateEventRequest request,
        CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        if (!TryNormalize(request, timeProvider.GetUtcNow().UtcDateTime, out NormalizedCreateEvent? values,
                out Dictionary<string, string[]>? fields))
        {
            return ValidationFailure(fields!);
        }

        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        Guid[] userIds = values!.Quotas
            .Select(quota => quota.UserId)
            .Distinct()
            .ToArray();
        Guid[] existingUserIds = await dbContext.AuthUsers
            .Where(user => user.Role == AuthRoles.User && userIds.Contains(user.Id))
            .Select(user => user.Id)
            .ToArrayAsync(cancellationToken);
        if (existingUserIds.Length != userIds.Length)
        {
            return NotFound(ApiResponse.Failure<object>("USER_NOT_FOUND", "Один или несколько пользователей не найдены."));
        }

        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var eventResult = EventItem.Create(values.Title, values.Description, values.StartAt, values.EndAt, nowUtc);
        if (eventResult.IsFailure)
        {
            return DomainFailure(eventResult.Error);
        }

        EventItem eventItem = eventResult.Value;
        List<EventQuota> quotas = [];
        foreach (InitialQuotaRequest quotaRequest in values.Quotas.Where(quota => quota.AvailableSeats > 0))
        {
            var quotaResult = EventQuota.Create(eventItem.Id, quotaRequest.UserId, quotaRequest.AvailableSeats);
            if (quotaResult.IsFailure)
            {
                return DomainFailure(quotaResult.Error);
            }

            quotas.Add(quotaResult.Value);
            eventItem.EventQuotas.Add(quotaResult.Value);
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.EventItems.Add(eventItem);
        dbContext.EventQuotas.AddRange(quotas);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Created(
            new Uri($"/api/admin/events/{eventItem.Id.Value:D}", UriKind.Relative),
            ApiResponse.Ok(ToResponse(eventItem, nowUtc, includeQuotas: true)));
    }

    [HttpPatch("admin/events/{eventId:guid}")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> UpdateEvent(
        Guid eventId,
        [FromBody] UpdateEventRequest request,
        CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem? eventItem = await dbContext.EventItems
            .SingleOrDefaultAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        if (eventItem is null)
        {
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));
        }

        string title = request.Title is null ? eventItem.Title! : request.Title.Trim();
        string? description = request.Description is null
            ? eventItem.Description
            : NormalizeDescription(request.Description);
        DateTime startAt = request.StartAt?.UtcDateTime ?? eventItem.StardAt;
        DateTime endAt = request.EndAt?.UtcDateTime ?? eventItem.EndAt;
        Dictionary<string, string[]> fields = ValidateDetails(title, description, startAt, endAt);
        if (fields.Count > 0)
        {
            return ValidationFailure(fields);
        }

        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var result = eventItem.Update(title, description, startAt, endAt, nowUtc);
        if (result.IsFailure)
        {
            return DomainFailure(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse.Ok(ToResponse(eventItem, nowUtc, includeQuotas: false)));
    }

    [HttpPost("admin/events/{eventId:guid}/publish")]
    [Authorize(Policy = "AdministratorOnly")]
    public Task<IActionResult> PublishEvent(Guid eventId, CancellationToken cancellationToken) =>
        ChangeStatusAsync(eventId, (eventItem, nowUtc) => eventItem.Publish(nowUtc), cancellationToken);

    [HttpPost("admin/events/{eventId:guid}/cancel")]
    [Authorize(Policy = "AdministratorOnly")]
    public Task<IActionResult> CancelEvent(Guid eventId, CancellationToken cancellationToken) =>
        ChangeStatusAsync(eventId, (eventItem, nowUtc) => eventItem.Cancel(nowUtc), cancellationToken);

    [HttpGet("events")]
    [Authorize]
    public async Task<IActionResult> GetUserEvents(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(ApiResponse.Failure<object>("UNAUTHENTICATED", "Требуется повторный вход."));
        }

        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem[] events = await dbContext.EventItems
            .AsNoTracking()
            .Where(item => item.Status == StatusEvent.Published
                && item.EventQuotas.Any(quota => quota.UserId == userId && quota.AvailableSeats > 0))
            .Include(item => item.EventQuotas.Where(quota => quota.UserId == userId && quota.AvailableSeats > 0))
            .OrderBy(item => item.StardAt)
            .ToArrayAsync(cancellationToken);
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        return Ok(ApiResponse.Ok(events.Select(item =>
            ToResponse(item, nowUtc, item.EventQuotas.Single(), includeQuotas: false))));
    }

    [HttpGet("events/{eventId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetUserEvent(Guid eventId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(ApiResponse.Failure<object>("UNAUTHENTICATED", "Требуется повторный вход."));
        }

        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem? eventItem = await dbContext.EventItems
            .AsNoTracking()
            .Where(item => item.Id == new EventItemId(eventId)
                && item.Status == StatusEvent.Published)
            .Include(item => item.EventQuotas.Where(quota => quota.UserId == userId && quota.AvailableSeats > 0))
            .SingleOrDefaultAsync(cancellationToken);
        EventQuota? quota = eventItem?.EventQuotas.SingleOrDefault();
        return eventItem is null || quota is null
            ? NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."))
            : Ok(ApiResponse.Ok(ToResponse(eventItem, timeProvider.GetUtcNow().UtcDateTime, quota)));
    }

    private async Task<IActionResult> ChangeStatusAsync(
        Guid eventId,
        Func<EventItem, DateTime, CSharpFunctionalExtensions.Result<EventItem, DomainError>> changeStatus,
        CancellationToken cancellationToken)
    {
        await completionService.CompleteEndedEventsAsync(cancellationToken);
        await using KapeldinerServiceDbContext dbContext =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        EventItem? eventItem = await dbContext.EventItems
            .SingleOrDefaultAsync(item => item.Id == new EventItemId(eventId), cancellationToken);
        if (eventItem is null)
        {
            return NotFound(ApiResponse.Failure<object>("EVENT_NOT_FOUND", "Мероприятие не найдено."));
        }

        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var result = changeStatus(eventItem, nowUtc);
        if (result.IsFailure)
        {
            return DomainFailure(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse.Ok(ToResponse(eventItem, nowUtc, includeQuotas: false)));
    }

    private IActionResult DomainFailure(DomainError error)
    {
        if (error.Code.StartsWith("GeneralErrors.", StringComparison.Ordinal))
        {
            return BadRequest(ApiResponse.Failure<object>("VALIDATION_FAILED", error.Message));
        }

        return Conflict(ApiResponse.Failure<object>(error.Code, error.Message));
    }

    private BadRequestObjectResult ValidationFailure(IReadOnlyDictionary<string, string[]> fields) =>
        BadRequest(new ApiEnvelope<object>(false, null,
            new ApiError("VALIDATION_FAILED", "Проверьте введенные данные.", fields)));

    private static bool TryNormalize(
        CreateEventRequest request,
        DateTime nowUtc,
        out NormalizedCreateEvent? values,
        out Dictionary<string, string[]>? fields)
    {
        string title = request.Title?.Trim() ?? string.Empty;
        string? description = NormalizeDescription(request.Description);
        DateTime startAt = request.StartAt?.UtcDateTime ?? DateTime.MinValue;
        DateTime endAt = request.EndAt?.UtcDateTime ?? DateTime.MinValue;
        fields = ValidateDetails(title, description, startAt, endAt);

        if (request.StartAt is null)
            fields["startAt"] = ["Укажите время начала мероприятия."];
        if (request.EndAt is null)
            fields["endAt"] = ["Укажите время окончания мероприятия."];
        if (request.StartAt is not null && startAt <= nowUtc)
            fields["startAt"] = ["Время начала должно быть в будущем."];

        InitialQuotaRequest[] quotas = request.Quotas?.ToArray() ?? [];
        if (quotas.Any(quota => quota.UserId == Guid.Empty || quota.AvailableSeats < 0))
            fields["quotas"] = ["Укажите корректные идентификаторы пользователей и неотрицательные квоты."];
        if (quotas.Select(quota => quota.UserId).Distinct().Count() != quotas.Length)
            fields["quotas"] = ["Нельзя указывать пользователя в квотах несколько раз."];

        values = fields.Count == 0
            ? new NormalizedCreateEvent(title, description, startAt, endAt, quotas)
            : null;
        return values is not null;
    }

    private static Dictionary<string, string[]> ValidateDetails(
        string title,
        string? description,
        DateTime startAt,
        DateTime endAt)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (title.Length is < 1 or > 200)
            fields["title"] = ["Название должно содержать от 1 до 200 символов."];
        if (description is { Length: > 2000 })
            fields["description"] = ["Описание не должно превышать 2000 символов."];
        if (startAt != DateTime.MinValue && endAt != DateTime.MinValue && endAt <= startAt)
            fields["endAt"] = ["Время окончания должно быть позже времени начала."];
        return fields;
    }

    private static string? NormalizeDescription(string? description)
    {
        string? normalized = description?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private bool TryGetCurrentUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static EventResponse ToResponse(
        EventItem eventItem,
        DateTime nowUtc,
        EventQuota? myQuota = null,
        bool includeQuotas = false)
    {
        EventQuotaResponse? quotaResponse = myQuota is null
            ? null
            : new EventQuotaResponse(
                myQuota.AvailableSeats,
                myQuota.UsedSeats,
                myQuota.AvailableSeats - myQuota.UsedSeats);
        EventQuotaResponse[]? quotas = includeQuotas
            ? eventItem.EventQuotas
                .OrderBy(quota => quota.UserId)
                .Select(quota => new EventQuotaResponse(
                    quota.AvailableSeats,
                    quota.UsedSeats,
                    quota.AvailableSeats - quota.UsedSeats,
                    quota.UserId))
                .ToArray()
            : null;
        return new EventResponse(
            eventItem.Id.Value,
            eventItem.Title!,
            eventItem.Description,
            AsMoscowOffset(eventItem.StardAt),
            AsMoscowOffset(eventItem.EndAt),
            GetStatusName(eventItem.Status),
            AsUtcOffset(eventItem.CreatedAt),
            eventItem.Status == StatusEvent.Published
                && nowUtc < eventItem.StardAt
                && nowUtc < eventItem.EndAt,
            quotaResponse,
            quotas);
    }

    private static DateTimeOffset AsUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset AsMoscowOffset(DateTime value) =>
        TimeZoneInfo.ConvertTime(AsUtcOffset(value), EventTimeZone);

    private static string GetStatusName(StatusEvent status) => status switch
    {
        StatusEvent.Draft => "draft",
        StatusEvent.Published => "published",
        StatusEvent.Completed => "completed",
        StatusEvent.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public sealed record CreateEventRequest(
        string? Title,
        string? Description,
        DateTimeOffset? StartAt,
        DateTimeOffset? EndAt,
        IReadOnlyList<InitialQuotaRequest>? Quotas);

    public sealed record InitialQuotaRequest(Guid UserId, int AvailableSeats);

    public sealed record UpdateEventRequest(
        string? Title,
        string? Description,
        DateTimeOffset? StartAt,
        DateTimeOffset? EndAt);

    public sealed record EventResponse(
        Guid EventId,
        string Title,
        string? Description,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        string Status,
        DateTimeOffset CreatedAt,
        bool BookingOpen,
        EventQuotaResponse? MyQuota,
        IReadOnlyList<EventQuotaResponse>? Quotas);

    public sealed record EventQuotaResponse(
        int AvailableSeats,
        int UsedSeats,
        int RemainingSeats,
        Guid? UserId = null);

    private sealed record NormalizedCreateEvent(
        string Title,
        string? Description,
        DateTime StartAt,
        DateTime EndAt,
        InitialQuotaRequest[] Quotas);
}
