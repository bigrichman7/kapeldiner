using CSharpFunctionalExtensions;
using KapeldinerService.Domain.Common.Errors;
using KapeldinerService.Domain.Events;
using System.Collections.ObjectModel;

namespace KapeldinerService.Domain.Reservations;

public record ReservationId(Guid Value);

public class Reservation
{

    private readonly List<ReservationSeat> _reservedSeats = [];
    public Reservation(ReservationId id, EventItemId eventItemId, Guid userId, IEnumerable<SeatId> seatIds, EventQuota eventQuota)
    {
        Id = id;
        EventItemId = eventItemId;
        UserId = userId;
        EventQuotaId = eventQuota.Id;

        var reservedSeats = seatIds
            .Select(seatId => new ReservationSeat(new ReservationSeatId(Guid.CreateVersion7()), Id, seatId));

        _reservedSeats.AddRange(reservedSeats);
    }

    public ReservationId Id { get; }
    public Guid UserId { get; }
    public EventItemId EventItemId { get; }
    public IReadOnlyList<ReservationSeat> ReservationSeats => _reservedSeats.AsReadOnly();

    public EventQuotaId EventQuotaId { get; }

    public static Result<Reservation, DomainError> Create(EventItemId eventItemId, Guid userId, IEnumerable<SeatId> seatIds, EventQuota eventQuota)
    {
        if (eventItemId.Value == Guid.Empty)
        {
            return GeneralErrors.ValueIsRequired(nameof(eventItemId));
        }

        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsRequired("UserId");
        }

        var listSeatIds = seatIds.ToList();
        if (listSeatIds == null)
        {
            return GeneralErrors.ValueIsRequired("ReservationSeatId");
        }

        if (listSeatIds.ToList().Count >= eventQuota.AvailableSeats - eventQuota.UsedSeats)
        {
            return GeneralErrors.ValueIsInvalid("EventQuota");
        }

        return new Reservation(new ReservationId(Guid.CreateVersion7()), eventItemId, userId, seatIds, eventQuota);
    }
}