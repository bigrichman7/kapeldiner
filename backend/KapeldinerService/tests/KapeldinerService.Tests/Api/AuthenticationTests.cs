using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace KapeldinerService.Tests.Api;

public sealed class AuthenticationTests : IClassFixture<AuthenticationApiFactory>
{
    private const string TestRoleHeader = "X-Test-Role";
    private readonly HttpClient _client;

    public AuthenticationTests(AuthenticationApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task ProtectedSessionRequiresAuthentication()
    {
        using HttpResponseMessage response = await _client.GetAsync(new Uri("/api/auth/session", UriKind.Relative));
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHENTICATED", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdministratorRouteRejectsRegularUserAndAcceptsAdministrator()
    {
        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/session");
        userRequest.Headers.Add(TestRoleHeader, "User");
        using HttpResponseMessage userResponse = await _client.SendAsync(userRequest);

        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/session");
        adminRequest.Headers.Add(TestRoleHeader, "Administrator");
        using HttpResponseMessage adminResponse = await _client.SendAsync(adminRequest);

        Assert.Equal(HttpStatusCode.Forbidden, userResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task UserManagementRoutesEnforceAdministratorAndAuthenticatedAccess()
    {
        using var anonymousRequest = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        using HttpResponseMessage anonymousResponse = await _client.SendAsync(anonymousRequest);

        using var regularUserRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        regularUserRequest.Headers.Add(TestRoleHeader, "User");
        using HttpResponseMessage regularUserResponse = await _client.SendAsync(regularUserRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, regularUserResponse.StatusCode);
    }

    [Fact]
    public async Task AdministratorCreateValidatesProfileFields()
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        tokenRequest.Headers.Add(TestRoleHeader, "Administrator");
        using HttpResponseMessage tokenResponse = await _client.SendAsync(tokenRequest);
        using JsonDocument tokenBody = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        string requestToken = tokenBody.RootElement.GetProperty("data").GetProperty("requestToken").GetString()!;

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = JsonContent.Create(new { login = "x", email = "not-an-email", name = " " })
        };
        createRequest.Headers.Add(TestRoleHeader, "Administrator");
        createRequest.Headers.Add("X-CSRF-TOKEN", requestToken);
        using HttpResponseMessage createResponse = await _client.SendAsync(createRequest);
        using JsonDocument body = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(body.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("login", out _));
        Assert.True(body.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("email", out _));
        Assert.True(body.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task LoginRequiresCsrfToken()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { login = "admin", password = "not-a-real-password" });
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CSRF_VALIDATION_FAILED", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AuthenticatedLogoutRequiresCsrfTokenAndAcceptsValidToken()
    {
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { })
        };
        invalidRequest.Headers.Add(TestRoleHeader, "User");
        using HttpResponseMessage invalidResponse = await _client.SendAsync(invalidRequest);

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        tokenRequest.Headers.Add(TestRoleHeader, "User");
        using HttpResponseMessage tokenResponse = await _client.SendAsync(tokenRequest);
        using JsonDocument tokenBody = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        string requestToken = tokenBody.RootElement.GetProperty("data").GetProperty("requestToken").GetString()!;
        string setCookieHeader = string.Join("; ", tokenResponse.Headers.GetValues("Set-Cookie"));

        using var validRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { })
        };
        validRequest.Headers.Add(TestRoleHeader, "User");
        validRequest.Headers.Add("X-CSRF-TOKEN", requestToken);
        using HttpResponseMessage validResponse = await _client.SendAsync(validRequest);

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        Assert.Contains("httponly", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
    }
}

public sealed class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }
}

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

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
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
