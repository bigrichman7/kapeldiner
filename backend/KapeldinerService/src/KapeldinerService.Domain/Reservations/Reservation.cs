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
            .Select(seat => new ReservationSeat(
                new ReservationSeatId(Guid.CreateVersion7()),
                Id,
                eventItemId,
                seat));

        _reservedSeats.AddRange(reservedSeats);
    }

    public ReservationId Id { get; } = null!;
    public Guid UserId { get; }
    public EventItemId EventItemId { get; } = null!;
    public IReadOnlyList<ReservationSeat> ReservationSeats => _reservedSeats;

    public EventQuotaId EventQuotaId { get; set; } = null!;

    public EventQuota EventQuota { get; set; } = null!;

    public EventItem EventItem { get; } = null!;

    public int RemoveSeats(IReadOnlySet<(int RowNumber, int SeatNumber)> seats)
    {
        int initialCount = _reservedSeats.Count;
        _reservedSeats.RemoveAll(reservationSeat =>
            seats.Contains((reservationSeat.Seat.RowNumber, reservationSeat.Seat.SeatNumber)));
        return initialCount - _reservedSeats.Count;
    }

    public int RemoveSeatIds(IReadOnlySet<string> seatIds)
    {
        int initialCount = _reservedSeats.Count;
        _reservedSeats.RemoveAll(reservationSeat => seatIds.Contains(reservationSeat.SeatId));
        return initialCount - _reservedSeats.Count;
    }

    public void ReplaceSeats(IEnumerable<Seat> seats)
    {
        Dictionary<string, Seat> desiredSeats = seats.ToDictionary(seat => seat.SeatId, StringComparer.Ordinal);
        _reservedSeats.RemoveAll(reservationSeat => !desiredSeats.ContainsKey(reservationSeat.SeatId));

        HashSet<string> currentSeats = _reservedSeats
            .Select(reservationSeat => reservationSeat.SeatId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (Seat seat in desiredSeats.Values.Where(seat => !currentSeats.Contains(seat.SeatId)))
        {
            _reservedSeats.Add(new ReservationSeat(
                new ReservationSeatId(Guid.CreateVersion7()),
                Id,
                EventItemId,
                seat));
        }
    }

    public static Result<Reservation, DomainError> Create(EventItemId eventItemId, Guid userId, IEnumerable<Seat> seats, EventQuota eventQuota)
    {
        if (eventItemId.Value == Guid.Empty || eventQuota.EventItemId != eventItemId)
        {
            return GeneralErrors.ValueIsRequired(nameof(eventItemId));
        }

        if (userId == Guid.Empty || eventQuota.UserId != userId)
        {
            return GeneralErrors.ValueIsRequired("UserId");
        }

        List<Seat> listSeatIds = seats.ToList();
        if (listSeatIds.Count == 0)
        {
            return GeneralErrors.ValueIsRequired("Seats");
        }

        if (listSeatIds.Select(seat => seat.SeatId).Distinct(StringComparer.Ordinal).Count() != listSeatIds.Count)
            return GeneralErrors.ValueIsInvalid("Seats");

        if (listSeatIds.Count > eventQuota.AvailableSeats - eventQuota.UsedSeats)
        {
            return GeneralErrors.ValueIsInvalid("EventQuota");
        }

        return new Reservation(new ReservationId(Guid.CreateVersion7()), eventItemId, userId, listSeatIds, eventQuota.Id);
    }
}