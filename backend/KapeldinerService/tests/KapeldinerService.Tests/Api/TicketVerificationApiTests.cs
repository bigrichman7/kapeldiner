using System.Net;
using System.Text.Json;
using KapeldinerService.Domain.Events;
using KapeldinerService.Domain.Reservations;
using KapeldinerService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KapeldinerService.Tests.Api;

[Collection("API integration tests")]
public sealed class TicketVerificationApiTests(UserManagementApiFactory factory)
    : IClassFixture<UserManagementApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task TicketVerificationIsPublicAndTracksEarlyFirstAndRepeatedScans()
    {
        await factory.EnsureDatabaseCreatedAsync();
        string earlyToken = await CreateTicketAsync(startInHours: 7);
        string validToken = await CreateTicketAsync(startInHours: 3);

        using HttpResponseMessage earlyResponse = await _client.GetAsync(new Uri($"/api/tickets/verify/{earlyToken}", UriKind.Relative));
        using JsonDocument earlyBody = await JsonDocument.ParseAsync(await earlyResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, earlyResponse.StatusCode);
        Assert.Equal("tooEarly", earlyBody.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("noindex, nofollow, noarchive", earlyResponse.Headers.GetValues("X-Robots-Tag").Single());
        await AssertTicketHasNotBeenVerifiedAsync(earlyToken);

        using HttpResponseMessage firstResponse = await _client.GetAsync(new Uri($"/api/tickets/verify/{validToken}", UriKind.Relative));
        using JsonDocument firstBody = await JsonDocument.ParseAsync(await firstResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("verified", firstBody.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.NotNull(firstBody.RootElement.GetProperty("data").GetProperty("eventTitle").GetString());
        await AssertTicketHasBeenVerifiedAsync(validToken);

        using HttpResponseMessage repeatedResponse = await _client.GetAsync(new Uri($"/api/tickets/verify/{validToken}", UriKind.Relative));
        using JsonDocument repeatedBody = await JsonDocument.ParseAsync(await repeatedResponse.Content.ReadAsStreamAsync());
        Assert.Equal("alreadyVerified", repeatedBody.RootElement.GetProperty("data").GetProperty("status").GetString());

        using HttpResponseMessage unknownResponse = await _client.GetAsync(
            new Uri($"/api/tickets/verify/{Guid.NewGuid():N}", UriKind.Relative));
        using JsonDocument unknownBody = await JsonDocument.ParseAsync(await unknownResponse.Content.ReadAsStreamAsync());
        Assert.Equal("notFound", unknownBody.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    private async Task<string> CreateTicketAsync(int startInHours)
    {
        await using KapeldinerServiceDbContext dbContext = await factory.CreateDbContextAsync();
        DateTime nowUtc = DateTime.UtcNow;
        EventItem eventItem = EventItem.Create(
            $"Ticket verification {Guid.NewGuid():N}",
            null,
            nowUtc.AddHours(startInHours),
            nowUtc.AddHours(startInHours + 2),
            nowUtc).Value;
        eventItem.Publish(nowUtc);
        Guid userId = Guid.NewGuid();
        EventQuota quota = EventQuota.Create(eventItem.Id, userId, 1).Value;
        Seat seat = Seat.Create("1", 1, SeatType.booked).Value;
        Reservation reservation = Reservation.Create(eventItem.Id, userId, [seat], quota).Value;
        quota.ReserveSeats(1);
        ReservationSeat reservationSeat = reservation.ReservationSeats.Single();

        dbContext.EventItems.Add(eventItem);
        dbContext.EventQuotas.Add(quota);
        dbContext.Set<Reservation>().Add(reservation);
        await dbContext.SaveChangesAsync();
        return reservationSeat.VerificationToken;
    }

    private async Task AssertTicketHasNotBeenVerifiedAsync(string token)
    {
        await using KapeldinerServiceDbContext dbContext = await factory.CreateDbContextAsync();
        ReservationSeat seat = await dbContext.Set<ReservationSeat>()
            .SingleAsync(ticket => ticket.VerificationToken == token);
        Assert.Null(seat.VerifiedAtUtc);
    }

    private async Task AssertTicketHasBeenVerifiedAsync(string token)
    {
        await using KapeldinerServiceDbContext dbContext = await factory.CreateDbContextAsync();
        ReservationSeat seat = await dbContext.Set<ReservationSeat>()
            .SingleAsync(ticket => ticket.VerificationToken == token);
        Assert.NotNull(seat.VerifiedAtUtc);
    }
}
