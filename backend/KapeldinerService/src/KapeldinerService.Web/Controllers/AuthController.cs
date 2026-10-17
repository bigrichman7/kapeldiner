using System.Security.Claims;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Web.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IDbContextFactory<KapeldinerServiceDbContext> contextFactory,
    IPasswordHasher<AuthUser> passwordHasher) : ControllerBase
{
    private static readonly AuthUser DummyUser = new()
    {
        Id = Guid.Empty,
        Login = "unknown",
        NormalizedLogin = "UNKNOWN",
        Name = "unknown",
        Email = "unknown@invalid.local",
        NormalizedEmail = "UNKNOWN@INVALID.LOCAL",
        PasswordHash = string.Empty,
        Role = AuthRoles.User,
        CreatedAtUtc = DateTime.UnixEpoch
    };

    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new PasswordHasher<AuthUser>().HashPassword(DummyUser, "not-a-real-user-password"));

    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult GetCsrfToken([FromServices] Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery)
    {
        Microsoft.AspNetCore.Antiforgery.AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(ApiResponse.Ok(new { requestToken = tokens.RequestToken }));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Login)
            || request.Login.Trim().Length > 50
            || string.IsNullOrEmpty(request.Password)
            || request.Password.Length > 1024)
        {
            return BadRequest(ApiResponse.Failure<object>("INVALID_CREDENTIALS", "Укажите логин и пароль."));
        }

        string normalizedLogin = request.Login.Trim().ToUpperInvariant();
        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        AuthUser? user = await dbContext.AuthUsers.SingleOrDefaultAsync(
            account => account.NormalizedLogin == normalizedLogin,
            cancellationToken);

        PasswordVerificationResult verification = user is null
            ? passwordHasher.VerifyHashedPassword(DummyUser, DummyPasswordHash.Value, request.Password)
            : passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (user is null || verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(ApiResponse.Failure<object>("INVALID_CREDENTIALS", "Неверный логин или пароль."));
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.Login),
            new(ClaimTypes.Role, user.Role)
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IssuedUtc = now,
                ExpiresUtc = now.AddHours(8),
                IsPersistent = false,
                AllowRefresh = true
            });

        return Ok(ApiResponse.Ok(new { id = user.Id, login = user.Login, role = user.Role }));
    }

    [HttpGet("session")]
    [Authorize]
    public IActionResult GetCurrentSession()
    {
        return Ok(ApiResponse.Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            login = User.Identity?.Name,
            role = User.FindFirstValue(ClaimTypes.Role)
        }));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(ApiResponse.Ok(new { signedOut = true }));
    }

    [HttpGet("/api/admin/session")]
    [Authorize(Policy = "AdministratorOnly")]
    public IActionResult GetAdministratorSession()
    {
        return Ok(ApiResponse.Ok(new { role = User.FindFirstValue(ClaimTypes.Role) }));
    }

    public sealed record LoginRequest(string? Login, string? Password);
}
