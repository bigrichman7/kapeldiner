using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using KapeldinerService.Domain.Events;
using KapeldinerService.Domain.Reservations;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KapeldinerService.Tests.Api;

[Collection("API integration tests")]
public sealed class PostgresReservationConcurrencyTests
{
    private static readonly string[] RequestedSeatIds = ["1-1"];

    [PostgresFact]
    public async Task ConcurrentAttemptsForTheSameSeatAllowOnlyOneBooking()
    {
        string? connectionString = Environment.GetEnvironmentVariable("KAPELDINER_TEST_POSTGRES_CONNECTION");

        await using var factory = new PostgresReservationApiFactory(connectionString!);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            KapeldinerServiceDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<IDbContextFactory<KapeldinerServiceDbContext>>()
                    .CreateDbContext();
            await using (dbContext)
            {
                await dbContext.Database.MigrateAsync();
                Guid firstUserId = Guid.NewGuid();
                Guid secondUserId = Guid.NewGuid();
                DateTime nowUtc = DateTime.UtcNow;
                EventItem eventItem = EventItem.Create(
                    "PostgreSQL reservation concurrency test",
                    null,
                    nowUtc.AddHours(2),
                    nowUtc.AddHours(3),
                    nowUtc).Value;
                Assert.True(eventItem.Publish(nowUtc).IsSuccess);
                dbContext.AuthUsers.AddRange(
                    CreateUser(firstUserId, "pg-concurrency-one"),
                    CreateUser(secondUserId, "pg-concurrency-two"));
                dbContext.EventItems.Add(eventItem);
                dbContext.EventQuotas.AddRange(
                    EventQuota.Create(eventItem.Id, firstUserId, 1).Value,
                    EventQuota.Create(eventItem.Id, secondUserId, 1).Value);
                await dbContext.SaveChangesAsync();

                using HttpClient client = factory.CreateClient();
                using HttpResponseMessage tokenResponse = await SendRequestAsync(
                    client,
                    HttpMethod.Get,
                    "/api/auth/csrf",
                    null,
                    null);
                using JsonDocument tokenBody =
                    await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
                string requestToken = tokenBody.RootElement
                    .GetProperty("data")
                    .GetProperty("requestToken")
                    .GetString()!;
                Task<HttpResponseMessage> firstRequest = SendAdminBookingAsync(
                    client,
                    eventItem.Id.Value,
                    firstUserId,
                    requestToken);
                Task<HttpResponseMessage> secondRequest = SendAdminBookingAsync(
                    client,
                    eventItem.Id.Value,
                    secondUserId,
                    requestToken);
                HttpResponseMessage[] responses = await Task.WhenAll(firstRequest, secondRequest);
                try
                {
                    string[] responseBodies = await Task.WhenAll(
                        responses.Select(response => response.Content.ReadAsStringAsync()));
                    Assert.True(
                        responses.All(response => response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict),
                        string.Join(Environment.NewLine, responseBodies));
                    Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
                    Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
                }
                finally
                {
                    foreach (HttpResponseMessage response in responses)
                        response.Dispose();
                }
                await dbContext.Entry(eventItem).ReloadAsync();
                int totalBookedSeats = await dbContext.Set<ReservationSeat>()
                    .CountAsync(seat => seat.EventItemId == eventItem.Id);
                int totalUsedQuota = await dbContext.EventQuotas
                    .Where(quota => quota.EventItemId == eventItem.Id)
                    .SumAsync(quota => quota.UsedSeats);
                Assert.Equal(1, totalBookedSeats);
                Assert.Equal(1, totalUsedQuota);

                dbContext.EventItems.Remove(eventItem);
                dbContext.AuthUsers.RemoveRange(
                    dbContext.AuthUsers.Where(user => user.Id == firstUserId || user.Id == secondUserId));
                await dbContext.SaveChangesAsync();
            }
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KAPELDINER_TEST_POSTGRES_CONNECTION")))
                Skip = "Set KAPELDINER_TEST_POSTGRES_CONNECTION to run the PostgreSQL concurrency test.";
        }
    }

    private static AuthUser CreateUser(Guid id, string login) => new()
    {
        Id = id,
        Login = login,
        NormalizedLogin = login.ToUpperInvariant(),
        Name = login,
        Email = $"{login}@example.com",
        NormalizedEmail = $"{login}@example.com".ToUpperInvariant(),
        PasswordHash = "not-used",
        Role = AuthRoles.User,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static async Task<HttpResponseMessage> SendAdminBookingAsync(
        HttpClient client,
        Guid eventId,
        Guid userId,
        string requestToken)
    {
        return await SendRequestAsync(
            client,
            HttpMethod.Put,
            $"/api/admin/events/{eventId:D}/quotas/{userId:D}/reservation",
            new { seatIds = RequestedSeatIds },
            requestToken);
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string? csrfToken,
        string? cookieHeader = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.Add("X-Test-Role", AuthRoles.Administrator);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        if (cookieHeader is not null)
            request.Headers.Add("Cookie", cookieHeader);
        return await client.SendAsync(request);
    }

    private sealed class PostgresReservationApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:KapeldinerServiceDb", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbContextFactory<KapeldinerServiceDbContext>>();
                services.RemoveAll<DbContextOptions<KapeldinerServiceDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KapeldinerServiceDbContext>>();
                services.AddDbContextFactory<KapeldinerServiceDbContext>(
                    options => options.UseNpgsql(connectionString));

                services.RemoveAll<IAntiforgery>();
                services.AddSingleton<IAntiforgery, PostgresTestAntiforgery>();

                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = PostgresTestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = PostgresTestAuthenticationHandler.SchemeName;
                        options.DefaultForbidScheme = PostgresTestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, PostgresTestAuthenticationHandler>(
                        PostgresTestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        }
    }

    private sealed class PostgresTestAntiforgery : IAntiforgery
    {
        private const string Token = "postgres-reservation-test-token";
        private static readonly AntiforgeryTokenSet Tokens = new(Token, Token, "RequestVerificationToken", "X-CSRF-TOKEN");

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => Tokens;

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => Tokens;

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);

        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;

        public void SetCookieTokenAndHeader(HttpContext httpContext)
        {
        }
    }

    private sealed class PostgresTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "PostgresReservationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out Microsoft.Extensions.Primitives.StringValues role)
                || string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());

            Claim[] claims =
            [
                new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new(ClaimTypes.Name, "postgres-test-admin"),
                new(ClaimTypes.Role, role.ToString())
            ];
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)),
                    SchemeName)));
        }
    }
}
