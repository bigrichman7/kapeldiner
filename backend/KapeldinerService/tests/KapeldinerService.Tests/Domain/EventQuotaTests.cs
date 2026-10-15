using KapeldinerService.Domain.Events;

namespace KapeldinerService.Tests.Domain;

public sealed class EventQuotaTests
{
    [Fact]
    public void CreateWithValidValuesReturnsQuotaWithAllSeatsAvailable()
    {
        EventItemId eventItemId = new(Guid.NewGuid());
        Guid userId = Guid.NewGuid();

        var result = EventQuota.Create(eventItemId, userId, 3);

        Assert.True(result.IsSuccess);
        Assert.Equal(eventItemId, result.Value.EventItemId);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal(3, result.Value.AvailableSeats);
        Assert.Equal(0, result.Value.UsedSeats);
    }

    [Fact]
    public void CreateWithNoAvailableSeatsReturnsValidationError()
    {
        var result = EventQuota.Create(new EventItemId(Guid.NewGuid()), Guid.NewGuid(), 0);

        Assert.True(result.IsFailure);
        Assert.Equal("GeneralErrors.ValueIsInvalid", result.Error.Code);
    }
}
