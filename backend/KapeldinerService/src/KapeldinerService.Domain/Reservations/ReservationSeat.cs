using KapeldinerService.Domain.Reservations;

namespace KapeldinerService.Domain.Reservations;

public record ReservationSeatId(Guid Value);

public class ReservationSeat
{
    public ReservationSeat(ReservationSeatId id, ReservationId reservationId, SeatId seatId)
    {
        Id = id;
        ReservationId = reservationId;
        SeatId = seatId;
        ReservedAt = DateTime.UtcNow;
    }
    public ReservationSeatId Id { get; }
    public ReservationId ReservationId { get; }

    public SeatId SeatId { get; }

    public DateTime ReservedAt { get; }
}