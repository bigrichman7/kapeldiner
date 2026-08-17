using CSharpFunctionalExtensions;
using KapeldinerService.Domain.Common.Errors;

namespace KapeldinerService.Domain.Reservations;

public record SeatId(Guid Value);

public record Seat
{
    public const int MaxRows = 27;
    public const int MaxSeats = 25;

    private Seat(SeatId id, int rowNumber, int seatNumber, SeatType status)
    {
        Id = id;
        RowNumber = rowNumber;
        SeatNumber = seatNumber;
        Status = status;
    }

    public SeatId Id { get; private set; }

    public int RowNumber { get; private set; }

    public int SeatNumber { get; private set; }

    public SeatType Status { get; private set; }

    public static Result<Seat, DomainError> Create(int rowNumber, int seatNumber, SeatType status)
    {
        if(rowNumber > MaxRows)
        {
            return GeneralErrors.ValueIsInvalid($"Параметр row не может быть больше {MaxRows}");
        }

        if (seatNumber > MaxSeats)
        {
            return GeneralErrors.ValueIsInvalid($"Параметр cell не может быть больше {MaxSeats}");
        }

        return new Seat(new SeatId(Guid.CreateVersion7()), rowNumber, seatNumber, status);
    }
}

