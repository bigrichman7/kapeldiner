using KapeldinerService.Domain.Events;

namespace KapeldinerService.Tests.Domain;

public sealed class EventItemTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateWithFutureDatesReturnsDraftEvent()
    {
        DateTime startAt = NowUtc.AddDays(1);
        DateTime endAt = startAt.AddHours(2);

        var result = EventItem.Create("Test event", null, startAt, endAt, NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal("Test event", result.Value.Title);
        Assert.Equal(StatusEvent.Draft, result.Value.Status);
    }

    [Fact]
    public void CreateRejectsStartAtInThePast()
    {
        var result = EventItem.Create("Test event", null, NowUtc, NowUtc.AddHours(2), NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("GeneralErrors.ValueIsInvalid", result.Error.Code);
    }

    [Fact]
    public void CreateWithEndBeforeStartReturnsValidationError()
    {
        DateTime startAt = NowUtc.AddDays(2);

        var result = EventItem.Create("Test event", null, startAt, startAt.AddHours(-1), NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("GeneralErrors.ValueIsInvalid", result.Error.Code);
    }

    [Fact]
    public void PublishAllowsOnlyDraftEvents()
    {
        EventItem eventItem = CreateEvent();

        Assert.True(eventItem.Publish(NowUtc).IsSuccess);
        Assert.Equal(StatusEvent.Published, eventItem.Status);

        var secondPublish = eventItem.Publish(NowUtc);

        Assert.True(secondPublish.IsFailure);
        Assert.Equal("EVENT_NOT_PUBLISHABLE", secondPublish.Error.Code);
    }

    [Fact]
    public void CancelledEventCannotBePublishedOrCompleted()
    {
        EventItem eventItem = CreateEvent();
        Assert.True(eventItem.Cancel(NowUtc).IsSuccess);

        Assert.True(eventItem.Publish(NowUtc).IsFailure);
        Assert.False(eventItem.RefreshStatus(eventItem.EndAt));
        Assert.Equal(StatusEvent.Cancelled, eventItem.Status);
    }

    [Fact]
    public void UpdateAllowsExtendingRunningEventButNotChangingItsStart()
    {
        EventItem eventItem = CreateEvent();
        Assert.True(eventItem.Publish(NowUtc).IsSuccess);
        DateTime startedAt = eventItem.StardAt;
        DateTime runningAt = startedAt.AddMinutes(10);

        var update = eventItem.Update("Updated", "Description", startedAt, eventItem.EndAt.AddHours(1), runningAt);
        var moveStart = eventItem.Update("Updated", null, startedAt.AddMinutes(1), eventItem.EndAt, runningAt);

        Assert.True(update.IsSuccess);
        Assert.Equal("Updated", eventItem.Title);
        Assert.True(moveStart.IsFailure);
        Assert.Equal("EVENT_START_ALREADY_REACHED", moveStart.Error.Code);
    }

    [Fact]
    public void EndTimeAutomaticallyCompletesEventAndPreventsChanges()
    {
        EventItem eventItem = CreateEvent();
        Assert.True(eventItem.Publish(NowUtc).IsSuccess);
        DateTime endAt = eventItem.EndAt;

        Assert.True(eventItem.RefreshStatus(endAt));
        Assert.Equal(StatusEvent.Completed, eventItem.Status);
        Assert.True(eventItem.Update("Changed", null, eventItem.StardAt, endAt.AddHours(1), endAt).IsFailure);
        Assert.True(eventItem.Cancel(endAt).IsFailure);
    }

    private static EventItem CreateEvent() =>
        EventItem.Create(
            "Test event",
            "Description",
            NowUtc.AddHours(1),
            NowUtc.AddHours(3),
            NowUtc).Value;
}
