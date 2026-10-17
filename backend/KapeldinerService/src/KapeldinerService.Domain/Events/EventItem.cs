using CSharpFunctionalExtensions;
using KapeldinerService.Domain.Common.Errors;
using KapeldinerService.Domain.Reservations;

namespace KapeldinerService.Domain.Events;

public record EventItemId(Guid Value);

public sealed class EventItem
{
    public EventItemId Id { get; private set; } = null!;
    public string? Title { get; private set; }
    public string? Description { get; private set; }

    public DateTime StardAt { get; private set; }

    public DateTime EndAt { get; private set; }

    public StatusEvent Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public bool EnableAdditionalRow12A { get; private set; }

    public ICollection<EventQuota> EventQuotas { get; } = new List<EventQuota>();

    public ICollection<Reservation> Reservations { get; } = new List<Reservation>();

    public ICollection<EventSeatDesignation> SeatDesignations { get; } = new List<EventSeatDesignation>();

    private EventItem() { }

    private EventItem(EventItemId id, string title, string? description, DateTime startAt, DateTime endAt, StatusEvent status, DateTime createdAt)
    {
        Id = id;
        Title = title;
        Description = description;
        StardAt = startAt;
        EndAt = endAt;
        Status = status;
        CreatedAt = createdAt;
    }

    public static Result<EventItem, DomainError> Create(
        string title,
        string? description,
        DateTime startAt,
        DateTime endAt,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(title))
            return GeneralErrors.ValueIsRequired("Title");

        if (startAt <= nowUtc)
            return GeneralErrors.ValueIsInvalid("StartAt должен быть позже текущего времени");

        if (startAt >= endAt)
            return GeneralErrors.ValueIsInvalid("EndAt должен быть позже StartAt");

        return new EventItem(
            new EventItemId(Guid.CreateVersion7()),
            title,
            description,
            startAt,
            endAt,
            StatusEvent.Draft,
            nowUtc);
    }

    public static Result<EventItem, DomainError> Create(
        string title,
        string? description,
        DateTime startAt,
        DateTime endAt,
        DateTime nowUtc,
        bool enableAdditionalRow12A)
    {
        var result = Create(title, description, startAt, endAt, nowUtc);
        if (result.IsSuccess)
            result.Value.EnableAdditionalRow12A = enableAdditionalRow12A;
        return result;
    }

    public Result<EventItem, DomainError> Update(
        string title,
        string? description,
        DateTime startAt,
        DateTime endAt,
        DateTime nowUtc,
        bool? enableAdditionalRow12A = null)
    {
        RefreshStatus(nowUtc);
        if (Status is StatusEvent.Completed or StatusEvent.Cancelled)
            return EventFailure("EVENT_NOT_EDITABLE", "Завершенное или отмененное мероприятие нельзя изменить.");

        if (string.IsNullOrWhiteSpace(title))
            return GeneralErrors.ValueIsRequired("Title");

        if (startAt >= endAt)
            return GeneralErrors.ValueIsInvalid("EndAt должен быть позже StartAt");

        if (endAt <= nowUtc)
            return GeneralErrors.ValueIsInvalid("EndAt должен быть позже текущего времени");

        if (nowUtc >= StardAt && startAt != StardAt)
            return EventFailure("EVENT_START_ALREADY_REACHED", "После начала мероприятия нельзя изменить время начала.");

        if (nowUtc < StardAt && startAt <= nowUtc)
            return GeneralErrors.ValueIsInvalid("StartAt должен быть позже текущего времени");

        Title = title;
        Description = description;
        StardAt = startAt;
        EndAt = endAt;
        if (enableAdditionalRow12A.HasValue)
            EnableAdditionalRow12A = enableAdditionalRow12A.Value;
        return this;
    }

    public Result<EventItem, DomainError> Publish(DateTime nowUtc)
    {
        RefreshStatus(nowUtc);
        if (Status != StatusEvent.Draft)
            return EventFailure("EVENT_NOT_PUBLISHABLE", "Опубликовать можно только черновик мероприятия.");

        if (StardAt <= nowUtc || EndAt <= nowUtc)
            return GeneralErrors.ValueIsInvalid("Время начала и окончания должно быть в будущем");

        Status = StatusEvent.Published;
        return this;
    }

    public Result<EventItem, DomainError> Cancel(DateTime nowUtc)
    {
        RefreshStatus(nowUtc);
        if (Status is StatusEvent.Completed or StatusEvent.Cancelled)
            return EventFailure("EVENT_NOT_CANCELLABLE", "Завершенное или отмененное мероприятие нельзя отменить.");

        Status = StatusEvent.Cancelled;
        return this;
    }

    public bool RefreshStatus(DateTime nowUtc)
    {
        if (Status is StatusEvent.Completed or StatusEvent.Cancelled || EndAt > nowUtc)
            return false;

        Status = StatusEvent.Completed;
        return true;
    }

    private static DomainError EventFailure(string code, string message) =>
        new(code, message, ErrorType.ValidationError);
}
