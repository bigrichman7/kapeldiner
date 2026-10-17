using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KapeldinerService.Domain.Events;
using KapeldinerService.Domain.Reservations;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Web.Auth;
using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Tests.Api;

[CollectionDefinition("API integration tests", DisableParallelization = true)]
public sealed class ApiIntegrationTestGroup
{
}

[Collection("API integration tests")]
public sealed class EventManagementApiTests : IClassFixture<UserManagementApiFactory>
{
    private static readonly Guid ExistingUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly string[] FirstCancelSeat = ["1-1"];
    private static readonly string[] SecondCancelSeat = ["1-2"];
    private readonly UserManagementApiFactory _factory;
    private readonly HttpClient _client;

    public EventManagementApiTests(UserManagementApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DraftVisibilityAndStatusTransitionsFollowEventAndQuotaAccess()
    {
        await PrepareDatabaseAsync();
        DateTimeOffset startAt = DateTimeOffset.UtcNow.AddHours(2);
        DateTimeOffset endAt = startAt.AddHours(3);
        using HttpResponseMessage createResponse = await SendAsAdminAsync(
            HttpMethod.Post,
            "/api/admin/events",
            new
            {
                title = "Opening performance",
                description = "First performance",
                startAt,
                endAt,
                quotas = new[] { new { userId = ExistingUserId, availableSeats = 5 } }
            });
        using JsonDocument createBody = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        Guid eventId = createBody.RootElement.GetProperty("data").GetProperty("eventId").GetGuid();

        using HttpResponseMessage hiddenDraftList = await SendAsUserAsync(HttpMethod.Get, "/api/events");
        using JsonDocument hiddenDraftListBody =
            await JsonDocument.ParseAsync(await hiddenDraftList.Content.ReadAsStreamAsync());
        using HttpResponseMessage hiddenDraft = await SendAsUserAsync(HttpMethod.Get, $"/api/events/{eventId:D}");
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(TimeSpan.FromHours(3),
            createBody.RootElement.GetProperty("data").GetProperty("startAt").GetDateTimeOffset().Offset);
        Assert.Empty(hiddenDraftListBody.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, hiddenDraft.StatusCode);

        using HttpResponseMessage publishResponse = await SendAsAdminAsync(
            HttpMethod.Post,
            $"/api/admin/events/{eventId:D}/publish");
        using HttpResponseMessage visibleList = await SendAsUserAsync(HttpMethod.Get, "/api/events");
        using JsonDocument visibleListBody =
            await JsonDocument.ParseAsync(await visibleList.Content.ReadAsStreamAsync());
        JsonElement visibleEvent = Assert.Single(visibleListBody.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        Assert.Equal("published", visibleEvent.GetProperty("status").GetString());
        Assert.True(visibleEvent.GetProperty("bookingOpen").GetBoolean());
        Assert.Equal(5, visibleEvent.GetProperty("myQuota").GetProperty("availableSeats").GetInt32());
        Assert.Equal(0, visibleEvent.GetProperty("myQuota").GetProperty("usedSeats").GetInt32());
        Assert.Equal(5, visibleEvent.GetProperty("myQuota").GetProperty("remainingSeats").GetInt32());

        using HttpResponseMessage adminDetail = await SendAsAdminAsync(
            HttpMethod.Get,
            $"/api/admin/events/{eventId:D}");
        using JsonDocument adminDetailBody =
            await JsonDocument.ParseAsync(await adminDetail.Content.ReadAsStreamAsync());
        Assert.Equal(5, Assert.Single(adminDetailBody.RootElement.GetProperty("data")
            .GetProperty("quotas").EnumerateArray()).GetProperty("availableSeats").GetInt32());
        Assert.Equal(5, Assert.Single(adminDetailBody.RootElement.GetProperty("data")
            .GetProperty("quotas").EnumerateArray()).GetProperty("remainingSeats").GetInt32());

        using HttpResponseMessage updateResponse = await SendAsAdminAsync(
            HttpMethod.Patch,
            $"/api/admin/events/{eventId:D}",
            new { title = "Updated performance", endAt = endAt.AddHours(1) });
        using JsonDocument updateBody = await JsonDocument.ParseAsync(await updateResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Updated performance", updateBody.RootElement.GetProperty("data").GetProperty("title").GetString());

        using HttpResponseMessage cancelResponse = await SendAsAdminAsync(
            HttpMethod.Post,
            $"/api/admin/events/{eventId:D}/cancel");
        using HttpResponseMessage hiddenCancelled = await SendAsUserAsync(HttpMethod.Get, $"/api/events/{eventId:D}");
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenCancelled.StatusCode);
    }

    [Fact]
    public async Task UserCannotSeePublishedEventWithoutPositiveQuota()
    {
        await PrepareDatabaseAsync();
        DateTimeOffset startAt = DateTimeOffset.UtcNow.AddHours(2);
        using HttpResponseMessage createResponse = await SendAsAdminAsync(
            HttpMethod.Post,
            "/api/admin/events",
            new
            {
                title = "Unassigned performance",
                startAt,
                endAt = startAt.AddHours(1),
                quotas = Array.Empty<object>()
            });
        using JsonDocument createBody = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        Guid eventId = createBody.RootElement.GetProperty("data").GetProperty("eventId").GetGuid();

        using HttpResponseMessage publishResponse = await SendAsAdminAsync(
            HttpMethod.Post,
            $"/api/admin/events/{eventId:D}/publish");
        using HttpResponseMessage detailResponse = await SendAsUserAsync(HttpMethod.Get, $"/api/events/{eventId:D}");
        using HttpResponseMessage listResponse = await SendAsUserAsync(HttpMethod.Get, "/api/events");
        using JsonDocument listBody = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
        Assert.Empty(listBody.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task InvalidEventDatesAreRejectedWithFieldValidation()
    {
        await PrepareDatabaseAsync();
        DateTimeOffset startAt = DateTimeOffset.UtcNow.AddHours(2);
        using HttpResponseMessage response = await SendAsAdminAsync(
            HttpMethod.Post,
            "/api/admin/events",
            new
            {
                title = "Invalid performance",
                startAt,
                endAt = startAt,
                quotas = Array.Empty<object>()
            });
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(body.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("endAt", out _));
    }

    [Fact]
    public async Task ApiReadPersistsCompletionOfEndedEvents()
    {
        await PrepareDatabaseAsync();
        DateTime nowUtc = DateTime.UtcNow;
        DateTime oldStart = nowUtc.AddDays(-2);
        EventItem endedEvent = EventItem.Create(
            "Ended performance",
            null,
            oldStart,
            nowUtc.AddMinutes(-1),
            nowUtc.AddDays(-3)).Value;
        await using (KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync())
        {
            dbContext.EventItems.Add(endedEvent);
            await dbContext.SaveChangesAsync();
        }

        using HttpResponseMessage response = await SendAsAdminAsync(HttpMethod.Get, "/api/admin/events");
        await using KapeldinerServiceDbContext verificationContext = await _factory.CreateDbContextAsync();
        EventItem savedEvent = await verificationContext.EventItems
            .SingleAsync(item => item.Id == endedEvent.Id);
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StatusEvent.Completed, savedEvent.Status);
        Assert.Equal("completed", body.RootElement.GetProperty("data").EnumerateArray()
            .Single(item => item.GetProperty("eventId").GetGuid() == endedEvent.Id.Value)
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunningPublishedEventIsVisibleButNotBookable()
    {
        await PrepareDatabaseAsync();
        DateTime nowUtc = DateTime.UtcNow;
        DateTime startAt = nowUtc.AddMinutes(-30);
        DateTime endAt = nowUtc.AddHours(1);
        EventItem runningEvent = EventItem.Create(
            "Running performance",
            null,
            startAt,
            endAt,
            nowUtc.AddHours(-1)).Value;
        Assert.True(runningEvent.Publish(nowUtc.AddHours(-1)).IsSuccess);
        EventQuota quota = EventQuota.Create(runningEvent.Id, ExistingUserId, 5).Value;
        await using (KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync())
        {
            dbContext.EventItems.Add(runningEvent);
            dbContext.EventQuotas.Add(quota);
            await dbContext.SaveChangesAsync();
        }

        using HttpResponseMessage response = await SendAsUserAsync(HttpMethod.Get, $"/api/events/{runningEvent.Id.Value:D}");
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.RootElement.GetProperty("data").GetProperty("bookingOpen").GetBoolean());
    }

    [Fact]
    public async Task AdministratorCanManageQuotasAndUsersOnlyReceiveTheirOwnQuota()
    {
        await PrepareDatabaseAsync();
        Guid secondUserId = Guid.NewGuid();
        await using (KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync())
        {
            dbContext.AuthUsers.Add(new AuthUser
            {
                Id = secondUserId,
                Login = "second-event-user",
                NormalizedLogin = "SECOND-EVENT-USER",
                Name = "Second Event User",
                Email = "second-event-user@example.com",
                NormalizedEmail = "SECOND-EVENT-USER@EXAMPLE.COM",
                PasswordHash = "not-used",
                Role = AuthRoles.User,
                CreatedAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        DateTimeOffset startAt = DateTimeOffset.UtcNow.AddHours(2);
        using HttpResponseMessage createResponse = await SendAsAdminAsync(
            HttpMethod.Post,
            "/api/admin/events",
            new
            {
                title = "Quota management performance",
                startAt,
                endAt = startAt.AddHours(2),
                quotas = Array.Empty<object>()
            });
        using JsonDocument createBody = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        Guid eventId = createBody.RootElement.GetProperty("data").GetProperty("eventId").GetGuid();
        using HttpResponseMessage publishResponse = await SendAsAdminAsync(
            HttpMethod.Post,
            $"/api/admin/events/{eventId:D}/publish");

        using HttpResponseMessage firstQuotaResponse = await SendAsAdminAsync(
            HttpMethod.Put,
            $"/api/admin/events/{eventId:D}/quotas/{ExistingUserId:D}",
            new { availableSeats = 4 });
        using HttpResponseMessage secondQuotaResponse = await SendAsAdminAsync(
            HttpMethod.Put,
            $"/api/admin/events/{eventId:D}/quotas/{secondUserId:D}",
            new { availableSeats = 8 });
        using HttpResponseMessage adminQuotasResponse = await SendAsAdminAsync(
            HttpMethod.Get,
            $"/api/admin/events/{eventId:D}/quotas");
        using JsonDocument adminQuotasBody =
            await JsonDocument.ParseAsync(await adminQuotasResponse.Content.ReadAsStreamAsync());
        using HttpResponseMessage adminQuotaResponse = await SendAsAdminAsync(
            HttpMethod.Get,
            $"/api/admin/events/{eventId:D}/quotas/{ExistingUserId:D}");
        using JsonDocument adminQuotaBody =
            await JsonDocument.ParseAsync(await adminQuotaResponse.Content.ReadAsStreamAsync());
        using HttpResponseMessage userEventsResponse = await SendAsUserAsync(HttpMethod.Get, "/api/events");
        using JsonDocument userEventsBody =
            await JsonDocument.ParseAsync(await userEventsResponse.Content.ReadAsStreamAsync());
        JsonElement userEvent = Assert.Single(userEventsBody.RootElement.GetProperty("data").EnumerateArray());

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstQuotaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondQuotaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminQuotaResponse.StatusCode);
        Assert.Equal(2, adminQuotasBody.RootElement.GetProperty("data").GetArrayLength());
        JsonElement adminQuota = adminQuotaBody.RootElement.GetProperty("data");
        Assert.Equal(4, adminQuota.GetProperty("availableSeats").GetInt32());
        Assert.Equal(0, adminQuota.GetProperty("usedSeats").GetInt32());
        Assert.Equal(4, adminQuota.GetProperty("remainingSeats").GetInt32());
        Assert.Equal("event-user", adminQuota.GetProperty("login").GetString());
        Assert.Equal(4, userEvent.GetProperty("myQuota").GetProperty("availableSeats").GetInt32());
        Assert.Equal(4, userEvent.GetProperty("myQuota").GetProperty("remainingSeats").GetInt32());
        Assert.Equal(JsonValueKind.Null, userEvent.GetProperty("quotas").ValueKind);
        Assert.Equal(JsonValueKind.Null, userEvent.GetProperty("myQuota").GetProperty("userId").ValueKind);

        using HttpResponseMessage updateResponse = await SendAsAdminAsync(
            HttpMethod.Put,
            $"/api/admin/events/{eventId:D}/quotas/{ExistingUserId:D}",
            new { availableSeats = 5 });
        using HttpResponseMessage userDetailResponse = await SendAsUserAsync(
            HttpMethod.Get,
            $"/api/events/{eventId:D}");
        using JsonDocument userDetailBody =
            await JsonDocument.ParseAsync(await userDetailResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(5, userDetailBody.RootElement.GetProperty("data")
            .GetProperty("myQuota").GetProperty("remainingSeats").GetInt32());

        using HttpResponseMessage invalidQuotaResponse = await SendAsAdminAsync(
            HttpMethod.Put,
            $"/api/admin/events/{eventId:D}/quotas/{ExistingUserId:D}",
            new { availableSeats = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidQuotaResponse.StatusCode);

        using HttpResponseMessage deleteResponse = await SendAsAdminAsync(
            HttpMethod.Delete,
            $"/api/admin/events/{eventId:D}/quotas/{ExistingUserId:D}");
        using HttpResponseMessage removedUserEvent = await SendAsUserAsync(HttpMethod.Get, $"/api/events/{eventId:D}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removedUserEvent.StatusCode);

        await using KapeldinerServiceDbContext cleanupContext = await _factory.CreateDbContextAsync();
        EventItem eventToDelete = await cleanupContext.EventItems.SingleAsync(item => item.Id == new EventItemId(eventId));
        cleanupContext.EventItems.Remove(eventToDelete);
        await cleanupContext.SaveChangesAsync();
    }

    [Fact]
    public async Task LoweringQuotaRequiresExactSeatCancellationsAndCannotDeleteActiveBookings()
    {
        await PrepareDatabaseAsync();
        DateTime nowUtc = DateTime.UtcNow;
        EventItem eventItem = EventItem.Create(
            "Quota reduction performance",
            null,
            nowUtc.AddHours(2),
            nowUtc.AddHours(4),
            nowUtc).Value;
        Assert.True(eventItem.Publish(nowUtc).IsSuccess);
        EventQuota quota = EventQuota.Create(eventItem.Id, ExistingUserId, 3).Value;
        Assert.True(quota.ReserveSeats(2).IsSuccess);
        Seat[] seats =
        [
            Seat.Create(1, 1, SeatType.booked).Value,
            Seat.Create(1, 2, SeatType.booked).Value
        ];
        var reservation = new Reservation(
            new ReservationId(Guid.NewGuid()),
            eventItem.Id,
            ExistingUserId,
            seats,
            quota.Id)
        {
            EventQuota = quota
        };
        quota.Reservation = reservation;
        eventItem.EventQuotas.Add(quota);
        await using (KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync())
        {
            dbContext.EventItems.Add(eventItem);
            dbContext.EventQuotas.Add(quota);
            dbContext.Add(reservation);
            await dbContext.SaveChangesAsync();
        }

        using HttpResponseMessage deleteWithBookings = await SendAsAdminAsync(
            HttpMethod.Delete,
            $"/api/admin/events/{eventItem.Id.Value:D}/quotas/{ExistingUserId:D}");
        using HttpResponseMessage missingCancellation = await SendAsAdminAsync(
            HttpMethod.Put,
            $"/api/admin/events/{eventItem.Id.Value:D}/quotas/{ExistingUserId:D}",
            new { availableSeats = 1 });
        using HttpResponseMessage firstCancellation = await SendAsAdminAsync(
            HttpMethod.Put,
            $"/api/admin/events/{eventItem.Id.Value:D}/quotas/{ExistingUserId:D}",
            new { availableSeats = 1, cancelSeatIds = FirstCancelSeat });

        Assert.Equal(HttpStatusCode.Conflict, deleteWithBookings.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingCancellation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstCancellation.StatusCode);
        await using (KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync())
        {
            EventQuota updatedQuota = await dbContext.EventQuotas
                .Include(item => item.Reservation)
                .ThenInclude(item => item!.ReservationSeats)
                .SingleAsync(item => item.EventItemId == eventItem.Id && item.UserId == ExistingUserId);
            Assert.Equal(1, updatedQuota.AvailableSeats);
            Assert.Equal(1, updatedQuota.UsedSeats);
            Seat remainingSeat = Assert.Single(updatedQuota.Reservation!.ReservationSeats).Seat;
            Assert.Equal((1, 2), (remainingSeat.RowNumber, remainingSeat.SeatNumber));
        }

        using HttpResponseMessage finalCancellation = await SendAsAdminAsync(
            HttpMethod.Put,
            $"/api/admin/events/{eventItem.Id.Value:D}/quotas/{ExistingUserId:D}",
            new { availableSeats = 0, cancelSeatIds = SecondCancelSeat });
        Assert.Equal(HttpStatusCode.OK, finalCancellation.StatusCode);
        await using (KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync())
        {
            EventQuota updatedQuota = await dbContext.EventQuotas
                .SingleAsync(item => item.EventItemId == eventItem.Id && item.UserId == ExistingUserId);
            Assert.Equal(0, updatedQuota.AvailableSeats);
            Assert.Equal(0, updatedQuota.UsedSeats);
            Assert.False(await dbContext.Set<Reservation>().AnyAsync(item => item.EventQuotaId == updatedQuota.Id));
        }

        await using KapeldinerServiceDbContext cleanupContext = await _factory.CreateDbContextAsync();
        EventItem eventToDelete = await cleanupContext.EventItems.SingleAsync(item => item.Id == eventItem.Id);
        cleanupContext.EventItems.Remove(eventToDelete);
        await cleanupContext.SaveChangesAsync();
    }

    private async Task PrepareDatabaseAsync()
    {
        await _factory.EnsureDatabaseCreatedAsync();
        await using KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync();
        if (!await dbContext.AuthUsers.AnyAsync(user => user.Id == ExistingUserId))
        {
            dbContext.AuthUsers.Add(new AuthUser
            {
                Id = ExistingUserId,
                Login = "event-user",
                NormalizedLogin = "EVENT-USER",
                Name = "Event User",
                Email = "event-user@example.com",
                NormalizedEmail = "EVENT-USER@EXAMPLE.COM",
                PasswordHash = "not-used",
                Role = AuthRoles.User,
                CreatedAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }
    }

    private Task<HttpResponseMessage> SendAsUserAsync(HttpMethod method, string path) =>
        SendWithRoleAsync(method, path, AuthRoles.User, null);

    private Task<HttpResponseMessage> SendAsAdminAsync(HttpMethod method, string path, object? body = null) =>
        SendWithRoleAsync(method, path, AuthRoles.Administrator, body);

    private async Task<HttpResponseMessage> SendWithRoleAsync(
        HttpMethod method,
        string path,
        string role,
        object? body)
    {
        string? requestToken = null;
        if (method != HttpMethod.Get)
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
            tokenRequest.Headers.Add("X-Test-Role", role);
            using HttpResponseMessage tokenResponse = await _client.SendAsync(tokenRequest);
            using JsonDocument tokenBody = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
            requestToken = tokenBody.RootElement.GetProperty("data").GetProperty("requestToken").GetString();
        }

        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.Add("X-Test-Role", role);
        if (requestToken is not null)
            request.Headers.Add("X-CSRF-TOKEN", requestToken);
        return await _client.SendAsync(request);
    }
}
