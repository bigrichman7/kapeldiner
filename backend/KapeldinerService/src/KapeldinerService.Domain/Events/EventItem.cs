using CSharpFunctionalExtensions;
using KapeldinerService.Domain.Common.Errors;

namespace KapeldinerService.Domain.Events;

public record EventItemId(Guid Value);

public sealed class EventItem
{
    public EventItemId Id { get; } = null!;
    public string? Title { get; }
    public string? Description { get; }

    public DateTime StardAt { get; }

    public DateTime EndAt { get; }

    public StatusEvent Status { get; }

    public DateTime CreatedAt { get; }

    private EventItem() { }

    private EventItem(EventItemId id, string title, string description, DateTime startAt, DateTime endAt, StatusEvent status, DateTime createdAt)
    {
        Id = id;
        Title = title;
        Description = description;
        StardAt = startAt;
        EndAt = endAt;
        Status = status;
        CreatedAt = createdAt;
    }

    public static Result<EventItem, DomainError> Create(string title, string description, DateTime startAt, DateTime endAt, StatusEvent status = StatusEvent.Draft)
    {
        if (string.IsNullOrWhiteSpace(title))
            return GeneralErrors.ValueIsRequired("Title");

        if (startAt <= DateTime.UtcNow || endAt <= DateTime.UtcNow)
            return GeneralErrors.ValueIsInvalid("StartAt и EndAt не могут быть раньше текущей даты");

        if (startAt >= endAt)
            return GeneralErrors.ValueIsInvalid("StartAt не может быть позже EndAt");

        return new EventItem(new EventItemId(Guid.CreateVersion7()), title, description, startAt, endAt, status, DateTime.UtcNow);
    }
}

