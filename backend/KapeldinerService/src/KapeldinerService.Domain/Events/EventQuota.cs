using CSharpFunctionalExtensions;
using KapeldinerService.Domain.Common.Errors;
using KapeldinerService.Domain.Reservations;

namespace KapeldinerService.Domain.Events;

public record EventQuotaId(Guid Value);

public record class EventQuota
{
    private EventQuota() { }

    private EventQuota(EventQuotaId id, EventItemId eventItemId, Guid userId, int availableSeats, int usedSeats)
    {
        Id = id;
        EventItemId = eventItemId;
        UserId = userId;
        AvailableSeats = availableSeats;
        UsedSeats = usedSeats;
    }

    public EventQuotaId Id { get; } = null!;
    public EventItemId EventItemId { get; } = null!;

    public EventItem EventItem { get; set; } = null!;
    public Guid UserId { get; private set; }
    public int AvailableSeats { get; private set; }
    public int UsedSeats { get; private set; }

    public Reservation? Reservation { get; set; }

    public static Result<EventQuota, DomainError> Create(EventItemId eventItemId, Guid userId, int availableSeats)
    {
        if (eventItemId.Value == Guid.Empty)
            return GeneralErrors.ValueIsInvalid("EventItemId");
        if (userId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid("UserId");
        if (availableSeats <= 0)
            return GeneralErrors.ValueIsInvalid("AvailableSeats");

        return new EventQuota(new EventQuotaId(Guid.CreateVersion7()), eventItemId, userId, availableSeats, 0);
    }
}