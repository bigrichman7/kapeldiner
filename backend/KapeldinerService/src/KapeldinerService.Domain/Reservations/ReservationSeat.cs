using KapeldinerService.Domain.Reservations;

namespace KapeldinerService.Domain.Reservations;

public record ReservationSeatId(Guid Value);

public class ReservationSeat
{

    public ReservationSeat() { }
    public ReservationSeat(ReservationSeatId id, ReservationId reservationId, Seat seat)
    {
        Id = id;
        ReservationId = reservationId;
        Seat = seat;
        ReservedAt = DateTime.UtcNow;
    }
    public ReservationSeatId Id { get; } = null!;
    public ReservationId ReservationId { get; } = null!;

    public Seat Seat { get; set; } = null!;

    public DateTime ReservedAt { get; }

    //Для связей EF-Core
    public Reservation Reservation { get; } = null!;
}