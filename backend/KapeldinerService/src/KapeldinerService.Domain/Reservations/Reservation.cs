using CSharpFunctionalExtensions;
using KapeldinerService.Domain.Common.Errors;
using KapeldinerService.Domain.Events;
using System.Collections.ObjectModel;

namespace KapeldinerService.Domain.Reservations;

public record ReservationId(Guid Value);

public class Reservation
{

    private readonly List<ReservationSeat> _reservedSeats = [];

    private Reservation() { }
    public Reservation(ReservationId id, EventItemId eventItemId, Guid userId, IEnumerable<Seat> seats, EventQuotaId eventQuotaId)
    {
        Id = id;
        EventItemId = eventItemId;
        UserId = userId;
        EventQuotaId = eventQuotaId;

        var reservedSeats = seats
            .Select(seat => new ReservationSeat(new ReservationSeatId(Guid.CreateVersion7()), Id, seat));

        _reservedSeats.AddRange(reservedSeats);
    }

    public ReservationId Id { get; } = null!;
    public Guid UserId { get; }
    public EventItemId EventItemId { get; } = null!;
    public IReadOnlyList<ReservationSeat> ReservationSeats => _reservedSeats;

    public EventQuotaId EventQuotaId { get; set; } = null!;

    public EventQuota EventQuota { get; set; } = null!;

    public EventItem EventItem { get; } = null!;



    public static Result<Reservation, DomainError> Create(EventItemId eventItemId, Guid userId, IEnumerable<Seat> seats, EventQuota eventQuota)
    {
        if (eventItemId.Value == Guid.Empty)
        {
            return GeneralErrors.ValueIsRequired(nameof(eventItemId));
        }

        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsRequired("UserId");
        }

        var listSeatIds = seats.ToList();
        if (listSeatIds == null)
        {
            return GeneralErrors.ValueIsRequired("ReservationSeatId");
        }

        if (listSeatIds.ToList().Count >= eventQuota.AvailableSeats - eventQuota.UsedSeats)
        {
            return GeneralErrors.ValueIsInvalid("EventQuota");
        }

        return new Reservation(new ReservationId(Guid.CreateVersion7()), eventItemId, userId, seats, eventQuota.Id);
    }
}