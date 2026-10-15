using KapeldinerService.Domain.Reservations;

namespace KapeldinerService.Tests.Domain;

public sealed class SeatTests
{
    [Fact]
    public void CreateWithValuesAtMaximumLimitsReturnsSeat()
    {
        var result = Seat.Create(Seat.MaxRows, Seat.MaxSeats, SeatType.vip);

        Assert.True(result.IsSuccess);
        Assert.Equal(Seat.MaxRows, result.Value.RowNumber);
        Assert.Equal(Seat.MaxSeats, result.Value.SeatNumber);
    }

    [Fact]
    public void CreateWithRowAboveMaximumReturnsValidationError()
    {
        var result = Seat.Create(Seat.MaxRows + 1, 1, SeatType.booked);

        Assert.True(result.IsFailure);
        Assert.Equal("GeneralErrors.ValueIsInvalid", result.Error.Code);
    }
}
