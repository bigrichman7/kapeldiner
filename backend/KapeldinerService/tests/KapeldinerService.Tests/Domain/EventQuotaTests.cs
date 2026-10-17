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
    public void CreateWithZeroAvailableSeatsReturnsQuotaWithNoCapacity()
    {
        var result = EventQuota.Create(new EventItemId(Guid.NewGuid()), Guid.NewGuid(), 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.AvailableSeats);
        Assert.Equal(0, result.Value.UsedSeats);
    }

    [Fact]
    public void UpdateCannotReduceQuotaBelowUsedSeats()
    {
        EventQuota quota = EventQuota.Create(new EventItemId(Guid.NewGuid()), Guid.NewGuid(), 3).Value;
        Assert.True(quota.ReserveSeats(2).IsSuccess);

        var result = quota.UpdateAvailableSeats(1);

        Assert.True(result.IsFailure);
        Assert.Equal("QUOTA_BELOW_USED_SEATS", result.Error.Code);
        Assert.Equal(3, quota.AvailableSeats);
        Assert.Equal(2, quota.UsedSeats);
    }
}
