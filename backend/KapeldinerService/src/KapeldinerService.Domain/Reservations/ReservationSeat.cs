using KapeldinerService.Domain.Reservations;
using KapeldinerService.Domain.Events;
using System.Security.Cryptography;

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
        VerificationToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
    public ReservationSeatId Id { get; } = null!;
    public ReservationId ReservationId { get; } = null!;

    public EventItemId EventItemId { get; private set; } = null!;

    public Seat Seat { get; private set; } = null!;

    public string SeatId => Seat.SeatId;

    public string SeatKey { get; private set; } = null!;

    public DateTime ReservedAt { get; }

    public string VerificationToken { get; private set; } = null!;

    public DateTime? VerifiedAtUtc { get; private set; }

    public void MarkVerified(DateTime verifiedAtUtc)
    {
        VerifiedAtUtc ??= DateTime.SpecifyKind(verifiedAtUtc, DateTimeKind.Utc);
    }

    //Для связей EF-Core
    public Reservation Reservation { get; } = null!;
}