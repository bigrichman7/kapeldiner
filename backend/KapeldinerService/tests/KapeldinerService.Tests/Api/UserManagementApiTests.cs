using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using System.Text.Encodings.Web;

namespace KapeldinerService.Tests.Api;

[Collection("API integration tests")]
public sealed class UserManagementApiTests : IClassFixture<UserManagementApiFactory>
{
    private const string TestRoleHeader = "X-Test-Role";
    private const string ExistingUserId = "00000000-0000-0000-0000-000000000001";
    private readonly UserManagementApiFactory _factory;
    private readonly HttpClient _client;

    public UserManagementApiTests(UserManagementApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task FailedCredentialDeliveryRollsBackUserCreation()
    {
        await _factory.EnsureDatabaseCreatedAsync();
        _factory.EmailSender.Result = EmailSendResult.Failed;

        using HttpResponseMessage response = await CreateUserAsync(
            $"delivery-{Guid.NewGuid():N}"[..18],
            $"delivery-{Guid.NewGuid():N}@example.com",
            "Delivery Failure");
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        await using KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync();
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("EMAIL_DELIVERY_FAILED", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, await dbContext.AuthUsers.CountAsync());
        Assert.False(string.IsNullOrEmpty(_factory.EmailSender.LastPassword));
        Assert.DoesNotContain(_factory.EmailSender.LastPassword!, body.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulCreationStoresOnlyHashAndRejectsDuplicateLoginCaseInsensitively()
    {
        await _factory.EnsureDatabaseCreatedAsync();
        _factory.EmailSender.Result = EmailSendResult.Sent;
        string login = $"created-{Guid.NewGuid():N}"[..18];
        string email = $"created-{Guid.NewGuid():N}@example.com";

        using HttpResponseMessage response = await CreateUserAsync(login, email, "Created User");
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Guid userId = body.RootElement.GetProperty("data").GetProperty("id").GetGuid();
        await using KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync();
        AuthUser user = await dbContext.AuthUsers.SingleAsync(account => account.Id == userId);
        IPasswordHasher<AuthUser> passwordHasher = _factory.Services.GetRequiredService<IPasswordHasher<AuthUser>>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(AuthRoles.User, user.Role);
        Assert.NotEqual(_factory.EmailSender.LastPassword, user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash, _factory.EmailSender.LastPassword!));
        Assert.DoesNotContain("password", body.RootElement.GetProperty("data").GetRawText(), StringComparison.OrdinalIgnoreCase);

        using HttpResponseMessage duplicateResponse = await CreateUserAsync(
            login.ToUpperInvariant(),
            $"different-{Guid.NewGuid():N}@example.com",
            "Duplicate User");
        using JsonDocument duplicateBody = await JsonDocument.ParseAsync(await duplicateResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal("LOGIN_ALREADY_EXISTS",
            duplicateBody.RootElement.GetProperty("error").GetProperty("code").GetString());

        using HttpResponseMessage duplicateEmailResponse = await CreateUserAsync(
            $"different-{Guid.NewGuid():N}"[..18],
            email.ToUpperInvariant(),
            "Duplicate Email");
        using JsonDocument duplicateEmailBody =
            await JsonDocument.ParseAsync(await duplicateEmailResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmailResponse.StatusCode);
        Assert.Equal("EMAIL_ALREADY_EXISTS",
            duplicateEmailBody.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdministratorCanReadUpdateAndDeleteRegularUsersButNotDeleteAdministrator()
    {
        await _factory.EnsureDatabaseCreatedAsync();
        _factory.EmailSender.Result = EmailSendResult.Sent;
        using HttpResponseMessage createResponse = await CreateUserAsync(
            $"admin-crud-{Guid.NewGuid():N}"[..18],
            $"admin-crud-{Guid.NewGuid():N}@example.com",
            "Admin CRUD User");
        using JsonDocument createBody = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        Guid userId = createBody.RootElement.GetProperty("data").GetProperty("id").GetGuid();

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        listRequest.Headers.Add(TestRoleHeader, AuthRoles.Administrator);
        using HttpResponseMessage listResponse = await _client.SendAsync(listRequest);
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{userId:D}");
        getRequest.Headers.Add(TestRoleHeader, AuthRoles.Administrator);
        using HttpResponseMessage getResponse = await _client.SendAsync(getRequest);
        using JsonDocument getBody = await JsonDocument.ParseAsync(await getResponse.Content.ReadAsStreamAsync());

        using HttpResponseMessage updateResponse = await SendUnsafeAsAdminAsync(
            HttpMethod.Put,
            $"/api/admin/users/{userId:D}",
            new
            {
                login = "admin-crud-updated",
                email = "admin-crud-updated@example.com",
                name = "Updated Admin CRUD User",
                role = AuthRoles.Administrator
            });
        using HttpResponseMessage deleteResponse = await SendUnsafeAsAdminAsync(
            HttpMethod.Delete,
            $"/api/admin/users/{userId:D}");

        await using KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync();
        dbContext.AuthUsers.Add(new AuthUser
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Login = "protected-admin",
            NormalizedLogin = "PROTECTED-ADMIN",
            Name = "Protected Admin",
            Email = "protected-admin@example.com",
            NormalizedEmail = "PROTECTED-ADMIN@EXAMPLE.COM",
            PasswordHash = "not-used",
            Role = AuthRoles.Administrator,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        using HttpResponseMessage adminDeleteResponse = await SendUnsafeAsAdminAsync(
            HttpMethod.Delete,
            "/api/admin/users/00000000-0000-0000-0000-000000000002");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(userId, getBody.RootElement.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, adminDeleteResponse.StatusCode);
        Assert.False(await dbContext.AuthUsers.AnyAsync(user => user.Id == userId));
    }

    [Fact]
    public async Task CurrentUserCannotChangeTheirRoleThroughProfileUpdate()
    {
        await _factory.EnsureDatabaseCreatedAsync();
        await using (KapeldinerServiceDbContext dbContext = await _factory.CreateDbContextAsync())
        {
            if (!await dbContext.AuthUsers.AnyAsync(user => user.Id == Guid.Parse(ExistingUserId)))
            {
                dbContext.AuthUsers.Add(new AuthUser
                {
                    Id = Guid.Parse(ExistingUserId),
                    Login = "member",
                    NormalizedLogin = "MEMBER",
                    Name = "Member",
                    Email = "member@example.com",
                    NormalizedEmail = "MEMBER@EXAMPLE.COM",
                    PasswordHash = "not-used",
                    Role = AuthRoles.User,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync();
            }
        }

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        tokenRequest.Headers.Add(TestRoleHeader, AuthRoles.User);
        using HttpResponseMessage tokenResponse = await _client.SendAsync(tokenRequest);
        using JsonDocument tokenBody = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        string requestToken = tokenBody.RootElement.GetProperty("data").GetProperty("requestToken").GetString()!;

        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, "/api/users/me")
        {
            Content = JsonContent.Create(new
            {
                login = "member-updated",
                email = "member-updated@example.com",
                name = "Updated Member",
                role = AuthRoles.Administrator
            })
        };
        updateRequest.Headers.Add(TestRoleHeader, AuthRoles.User);
        updateRequest.Headers.Add("X-CSRF-TOKEN", requestToken);
        using HttpResponseMessage updateResponse = await _client.SendAsync(updateRequest);
        await using KapeldinerServiceDbContext verificationContext = await _factory.CreateDbContextAsync();
        AuthUser updatedUser = await verificationContext.AuthUsers.SingleAsync(
            user => user.Id == Guid.Parse(ExistingUserId));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(AuthRoles.User, updatedUser.Role);
        Assert.Equal("Updated Member", updatedUser.Name);
        Assert.Equal("member-updated", updatedUser.Login);
    }

    private async Task<HttpResponseMessage> CreateUserAsync(string login, string email, string name)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        tokenRequest.Headers.Add(TestRoleHeader, AuthRoles.Administrator);
        using HttpResponseMessage tokenResponse = await _client.SendAsync(tokenRequest);
        using JsonDocument tokenBody = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        string requestToken = tokenBody.RootElement.GetProperty("data").GetProperty("requestToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = JsonContent.Create(new { login, email, name })
        };
        request.Headers.Add(TestRoleHeader, AuthRoles.Administrator);
        request.Headers.Add("X-CSRF-TOKEN", requestToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendUnsafeAsAdminAsync(
        HttpMethod method,
        string path,
        object? body = null)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        tokenRequest.Headers.Add(TestRoleHeader, AuthRoles.Administrator);
        using HttpResponseMessage tokenResponse = await _client.SendAsync(tokenRequest);
        using JsonDocument tokenBody = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        string requestToken = tokenBody.RootElement.GetProperty("data").GetProperty("requestToken").GetString()!;

        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.Add(TestRoleHeader, AuthRoles.Administrator);
        request.Headers.Add("X-CSRF-TOKEN", requestToken);
        return await _client.SendAsync(request);
    }
}

public sealed class UserManagementApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAliveConnection;

    public TestUserEmailSender EmailSender { get; } = new();

    public UserManagementApiFactory()
    {
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        await using KapeldinerServiceDbContext dbContext = await CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task<KapeldinerServiceDbContext> CreateDbContextAsync()
    {
        IDbContextFactory<KapeldinerServiceDbContext> contextFactory =
            Services.GetRequiredService<IDbContextFactory<KapeldinerServiceDbContext>>();
        return await contextFactory.CreateDbContextAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextFactory<KapeldinerServiceDbContext>>();
            services.RemoveAll<DbContextOptions<KapeldinerServiceDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<KapeldinerServiceDbContext>>();
            services.AddDbContextFactory<KapeldinerServiceDbContext>(
                options => options.UseSqlite(_connectionString));

            services.RemoveAll<IUserEmailSender>();
            services.AddSingleton<IUserEmailSender>(EmailSender);
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = UserManagementTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = UserManagementTestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = UserManagementTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, UserManagementTestAuthenticationHandler>(
                    UserManagementTestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keepAliveConnection.Dispose();
        }

        base.Dispose(disposing);
    }
}

public sealed class TestUserEmailSender : IUserEmailSender
{
    public EmailSendResult Result { get; set; } = EmailSendResult.Sent;

    public string? LastPassword { get; private set; }

    public Task<EmailSendResult> SendCredentialsAsync(
        string email,
        string name,
        string login,
        string password,
        CancellationToken cancellationToken)
    {
        LastPassword = password;
        return Task.FromResult(Result);
    }
}

public sealed class UserManagementTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "UserManagementTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out Microsoft.Extensions.Primitives.StringValues roleValue)
            || string.IsNullOrWhiteSpace(roleValue))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
            new(ClaimTypes.Name, "test-user"),
            new(ClaimTypes.Role, roleValue.ToString())
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
