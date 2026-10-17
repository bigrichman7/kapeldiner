using CSharpFunctionalExtensions;
using KapeldinerService.Domain.Common.Errors;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;

namespace KapeldinerService.Domain.Reservations;

public record Seat
{
    public const int MaxRows = 27;
    public const int MaxSeats = 26;

    private Seat(string rowLabel, int seatNumber, SeatType status)
    {
        RowLabel = rowLabel;
        SeatNumber = seatNumber;
        Status = status;
    }

    public string RowLabel { get; private set; } = null!;

    public int SeatNumber { get; private set; }

    public SeatType Status { get; private set; }

    [NotMapped]
    public int RowNumber =>
        int.TryParse(RowLabel, NumberStyles.None, CultureInfo.InvariantCulture, out int rowNumber)
            ? rowNumber
            : 0;

    public string SeatId => $"{RowLabel}-{SeatNumber.ToString(CultureInfo.InvariantCulture)}";

    public static Result<Seat, DomainError> Create(int rowNumber, int seatNumber, SeatType status)
    {
        if (rowNumber < 1 || rowNumber > MaxRows)
        {
            return GeneralErrors.ValueIsInvalid($"Параметр row не может быть больше {MaxRows}");
        }

        return Create(rowNumber.ToString(CultureInfo.InvariantCulture), seatNumber, status);
    }

    public static Result<Seat, DomainError> Create(string rowLabel, int seatNumber, SeatType status)
    {
        if (rowLabel is not ("12А" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9"
            or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20"
            or "21" or "22" or "23" or "24" or "25" or "26" or "27"))
            return GeneralErrors.ValueIsInvalid("Параметр row имеет недопустимое значение");

        if (seatNumber < 1 || seatNumber > MaxSeats)
        {
            return GeneralErrors.ValueIsInvalid($"Параметр cell не может быть больше {MaxSeats}");
        }

        if (!Enum.IsDefined(status))
            return GeneralErrors.ValueIsInvalid("Параметр status имеет недопустимое значение");

        return new Seat(rowLabel, seatNumber, status);
    }
}
