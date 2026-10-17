using KapeldinerService.Domain.Reservations;
using KapeldinerService.Domain.Events;

namespace KapeldinerService.Domain.Reservations;

public record ReservationSeatId(Guid Value);

public class ReservationSeat
{

    public ReservationSeat() { }
    public ReservationSeat(ReservationSeatId id, ReservationId reservationId, EventItemId eventItemId, Seat seat)
    {
        Id = id;
        ReservationId = reservationId;
        EventItemId = eventItemId;
        Seat = seat;
        ReservedAt = DateTime.UtcNow;
    }
    public ReservationSeatId Id { get; } = null!;
    public ReservationId ReservationId { get; } = null!;

    public EventItemId EventItemId { get; private set; } = null!;

    public Seat Seat { get; private set; } = null!;

    public string SeatId => Seat.SeatId;

    public string SeatKey { get; private set; } = null!;

    public DateTime ReservedAt { get; }

    //Для связей EF-Core
    public Reservation Reservation { get; } = null!;
}