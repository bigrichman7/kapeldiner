using KapeldinerService.Domain.Events;

namespace KapeldinerService.Tests.Domain;

public sealed class EventItemTests
{
    [Fact]
    public void CreateWithFutureDatesReturnsDraftEvent()
    {
        DateTime startAt = DateTime.UtcNow.AddDays(1);
        DateTime endAt = startAt.AddHours(2);

        var result = EventItem.Create("Test event", "Description", startAt, endAt);

        Assert.True(result.IsSuccess);
        Assert.Equal("Test event", result.Value.Title);
        Assert.Equal(StatusEvent.Draft, result.Value.Status);
    }

    [Fact]
    public void CreateWithEndBeforeStartReturnsValidationError()
    {
        DateTime startAt = DateTime.UtcNow.AddDays(2);
        DateTime endAt = startAt.AddHours(-1);

        var result = EventItem.Create("Test event", "Description", startAt, endAt);

        Assert.True(result.IsFailure);
        Assert.Equal("GeneralErrors.ValueIsInvalid", result.Error.Code);
    }
}
