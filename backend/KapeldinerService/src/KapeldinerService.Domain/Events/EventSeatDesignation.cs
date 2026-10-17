using KapeldinerService.Domain.Reservations;

namespace KapeldinerService.Domain.Events;

public enum SeatDesignation
{
    Organization,
    Vip
}

public sealed class EventSeatDesignation
{
    private EventSeatDesignation() { }

    public EventSeatDesignation(
        EventItemId eventItemId,
        string rowLabel,
        int seatNumber,
        SeatDesignation designation)
    {
        EventItemId = eventItemId;
        RowLabel = rowLabel;
        SeatNumber = seatNumber;
        Designation = designation;
    }

    public EventItemId EventItemId { get; private set; } = null!;

    public EventItem EventItem { get; private set; } = null!;

    public string RowLabel { get; private set; } = null!;

    public int SeatNumber { get; private set; }

    public SeatDesignation Designation { get; private set; }
}
