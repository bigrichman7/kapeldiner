using System.Globalization;
using System.Reflection;
using System.Text.Json;
using KapeldinerService.Domain.Reservations;

namespace KapeldinerService.Web.Reservations;

internal static class HallSeatCatalog
{
    private static readonly Dictionary<string, (string RowLabel, int SeatNumber)> Seats = LoadSeats();

    public static bool TryCreateSeat(string? seatId, bool additionalRowEnabled, out Seat? seat)
    {
        seat = null;
        if (seatId is null || !Seats.TryGetValue(seatId, out (string RowLabel, int SeatNumber) location))
            return false;

        if (string.Equals(location.RowLabel, "12А", StringComparison.Ordinal) && !additionalRowEnabled)
            return false;

        var result = Seat.Create(location.RowLabel, location.SeatNumber, SeatType.booked);
        if (result.IsFailure)
            return false;

        seat = result.Value;
        return true;
    }

    public static IReadOnlyList<Seat> GetAll(bool additionalRowEnabled) =>
        Seats
            .Where(item => additionalRowEnabled || !string.Equals(item.Value.RowLabel, "12А", StringComparison.Ordinal))
            .Select(item => Seat.Create(item.Value.RowLabel, item.Value.SeatNumber, SeatType.booked).Value)
            .ToArray();

    private static Dictionary<string, (string RowLabel, int SeatNumber)> LoadSeats()
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("KapeldinerService.Web.hall-layout.json")
            ?? throw new InvalidOperationException("The embedded hall seat catalogue is missing.");
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement seats = document.RootElement.GetProperty("seats");
        return seats.EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("seatId").GetString()
                    ?? throw new InvalidOperationException("A hall seat has no seatId."),
                item => (
                    item.GetProperty("rowLabel").GetString()
                        ?? throw new InvalidOperationException("A hall seat has no rowLabel."),
                    item.GetProperty("seatNumber").GetInt32()),
                StringComparer.Ordinal);
    }
}
