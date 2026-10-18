using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KapeldinerService.Web.Controllers;

[ApiController]
[Route("api")]
public sealed partial class UsersController(
    IDbContextFactory<KapeldinerServiceDbContext> contextFactory,
    IPasswordHasher<AuthUser> passwordHasher,
    IUserEmailSender emailSender,
    ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet("users/me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(ApiResponse.Failure<object>("UNAUTHENTICATED", "Требуется повторный вход."));
        }

        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        AuthUser? user = await dbContext.AuthUsers.SingleOrDefaultAsync(account => account.Id == userId, cancellationToken);
        return user is null
            ? NotFound(ApiResponse.Failure<object>("USER_NOT_FOUND", "Пользователь не найден."))
            : Ok(ApiResponse.Ok(ToResponse(user)));
    }

    [HttpPut("users/me")]
    [Authorize]
    public async Task<IActionResult> UpdateCurrentUser(
        [FromBody] UserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(ApiResponse.Failure<object>("UNAUTHENTICATED", "Требуется повторный вход."));
        }

        if (!TryNormalize(request.Login, request.Email, request.Name, out NormalizedUserData? values, out IActionResult? error))
        {
            return error!;
        }

        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        AuthUser? user = await dbContext.AuthUsers.SingleOrDefaultAsync(account => account.Id == userId, cancellationToken);
        if (user is null)
        {
            return NotFound(ApiResponse.Failure<object>("USER_NOT_FOUND", "Пользователь не найден."));
        }

        IActionResult? duplicateError = await GetDuplicateErrorAsync(dbContext, values!, userId, cancellationToken);
        if (duplicateError is not null)
        {
            return duplicateError;
        }

        bool loginChanged = !string.Equals(user.NormalizedLogin, values!.NormalizedLogin, StringComparison.Ordinal);
        user.Login = values.Login;
        user.NormalizedLogin = values.NormalizedLogin;
        user.Name = values.Name;
        user.Email = values.Email;
        user.NormalizedEmail = values.NormalizedEmail;
        if (loginChanged)
        {
            await RevokeUserSessionsAsync(dbContext, userId, cancellationToken);
        }

        if (!await TrySaveChangesAsync(dbContext, cancellationToken))
        {
            return Conflict(UniqueConflictBody());
        }

        return Ok(ApiResponse.Ok(new ProfileUpdateResponse(ToResponse(user), loginChanged)));
    }

    [HttpGet("admin/users")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        AuthUser[] users = await dbContext.AuthUsers
            .OrderBy(user => user.Login)
            .ToArrayAsync(cancellationToken);
        return Ok(ApiResponse.Ok(users.Select(ToResponse)));
    }

    [HttpGet("admin/users/{id:guid}")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> GetUserById(Guid id, CancellationToken cancellationToken)
    {
        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        AuthUser? user = await dbContext.AuthUsers.SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
        return user is null
            ? NotFound(ApiResponse.Failure<object>("USER_NOT_FOUND", "Пользователь не найден."))
            : Ok(ApiResponse.Ok(ToResponse(user)));
    }

    [HttpPost("admin/users")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> CreateUser(
        [FromBody] UserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(request.Login, request.Email, request.Name, out NormalizedUserData? values, out IActionResult? error))
        {
            return error!;
        }

        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        IActionResult? duplicateError = await GetDuplicateErrorAsync(dbContext, values!, null, cancellationToken);
        if (duplicateError is not null)
        {
            return duplicateError;
        }

        string password = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(6));
        var user = new AuthUser
        {
            Id = Guid.NewGuid(),
            Login = values!.Login,
            NormalizedLogin = values.NormalizedLogin,
            Name = values.Name,
            Email = values.Email,
            NormalizedEmail = values.NormalizedEmail,
            PasswordHash = string.Empty,
            Role = AuthRoles.User,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.AuthUsers.Add(user);
        if (!await TrySaveChangesAsync(dbContext, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(UniqueConflictBody());
        }

        EmailSendResult emailResult = await emailSender.SendCredentialsAsync(
            user.Email, user.Name, user.Login, password, cancellationToken);
        if (emailResult == EmailSendResult.NotConfigured)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogEmailNotConfigured(logger);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse.Failure<object>("EMAIL_NOT_CONFIGURED", "Отправка электронной почты не настроена; пользователь не создан."));
        }

        if (emailResult == EmailSendResult.Failed)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogEmailDeliveryFailed(logger);
            return StatusCode(StatusCodes.Status502BadGateway,
                ApiResponse.Failure<object>("EMAIL_DELIVERY_FAILED", "Не удалось отправить письмо; пользователь не создан."));
        }

        await transaction.CommitAsync(cancellationToken);
        return Created(new Uri($"/api/admin/users/{user.Id:D}", UriKind.Relative), ApiResponse.Ok(ToResponse(user)));
    }

    [HttpPut("admin/users/{id:guid}")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        [FromBody] UserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(request.Login, request.Email, request.Name, out NormalizedUserData? values, out IActionResult? error))
        {
            return error!;
        }

        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        AuthUser? user = await dbContext.AuthUsers.SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound(ApiResponse.Failure<object>("USER_NOT_FOUND", "Пользователь не найден."));
        }

        IActionResult? duplicateError = await GetDuplicateErrorAsync(dbContext, values!, id, cancellationToken);
        if (duplicateError is not null)
        {
            return duplicateError;
        }

        bool loginChanged = !string.Equals(user.NormalizedLogin, values!.NormalizedLogin, StringComparison.Ordinal);
        user.Login = values.Login;
        user.NormalizedLogin = values.NormalizedLogin;
        user.Name = values.Name;
        user.Email = values.Email;
        user.NormalizedEmail = values.NormalizedEmail;
        if (loginChanged)
        {
            await RevokeUserSessionsAsync(dbContext, id, cancellationToken);
        }

        if (!await TrySaveChangesAsync(dbContext, cancellationToken))
        {
            return Conflict(UniqueConflictBody());
        }

        return Ok(ApiResponse.Ok(new ProfileUpdateResponse(ToResponse(user), loginChanged)));
    }

    [HttpDelete("admin/users/{id:guid}")]
    [Authorize(Policy = "AdministratorOnly")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        AuthUser? user = await dbContext.AuthUsers.SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound(ApiResponse.Failure<object>("USER_NOT_FOUND", "Пользователь не найден."));
        }

        if (string.Equals(user.Role, AuthRoles.Administrator, StringComparison.Ordinal))
        {
            return Conflict(ApiResponse.Failure<object>("ADMINISTRATOR_PROTECTED", "Учетную запись единственного администратора удалить нельзя."));
        }

        dbContext.AuthUsers.Remove(user);
        await RevokeUserSessionsAsync(dbContext, id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse.Ok(new { deleted = true }));
    }

    private async Task<IActionResult?> GetDuplicateErrorAsync(
        KapeldinerServiceDbContext dbContext,
        NormalizedUserData values,
        Guid? excludingUserId,
        CancellationToken cancellationToken)
    {
        IQueryable<AuthUser> users = dbContext.AuthUsers;
        if (excludingUserId.HasValue)
        {
            users = users.Where(user => user.Id != excludingUserId.Value);
        }

        if (await users.AnyAsync(user => user.NormalizedLogin == values.NormalizedLogin, cancellationToken))
        {
            return Conflict(new ApiEnvelope<object>(false, default,
                new ApiError("LOGIN_ALREADY_EXISTS", "Этот логин уже используется.",
                    new Dictionary<string, string[]>(StringComparer.Ordinal) { ["login"] = ["Логин уже используется."] })));
        }

        if (await users.AnyAsync(user => user.NormalizedEmail == values.NormalizedEmail, cancellationToken))
        {
            return Conflict(new ApiEnvelope<object>(false, default,
                new ApiError("EMAIL_ALREADY_EXISTS", "Этот email уже используется.",
                    new Dictionary<string, string[]>(StringComparer.Ordinal) { ["email"] = ["Email уже используется."] })));
        }

        return null;
    }

    private static async Task<bool> TrySaveChangesAsync(
        KapeldinerServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static ApiEnvelope<object> UniqueConflictBody() =>
        ApiResponse.Failure<object>("USER_UNIQUE_CONFLICT", "Логин или email уже используется.");


    private static bool TryNormalize(
        string? login,
        string? email,
        string? name,
        out NormalizedUserData? values,
        out IActionResult? error)
    {
        string normalizedLogin = login?.Trim() ?? string.Empty;
        string normalizedEmail = email?.Trim() ?? string.Empty;
        string normalizedName = name?.Trim() ?? string.Empty;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (normalizedLogin.Length is < 3 or > 50
            || normalizedLogin.Any(character => !char.IsLetterOrDigit(character) && character is not ('.' or '_' or '-')))
        {
            fields["login"] = ["Логин должен содержать 3–50 символов: буквы, цифры, точку, дефис или подчеркивание."];
        }

        if (normalizedName.Length is < 1 or > 100)
        {
            fields["name"] = ["Имя должно содержать от 1 до 100 символов."];
        }

        if (normalizedEmail.Length > 254 || !IsValidEmail(normalizedEmail))
        {
            fields["email"] = ["Укажите корректный email длиной не более 254 символов."];
        }

        if (fields.Count > 0)
        {
            error = new BadRequestObjectResult(new ApiEnvelope<object>(false, default,
                new ApiError("VALIDATION_FAILED", "Проверьте введенные данные.", fields)));
            values = null;
            return false;
        }

        values = new NormalizedUserData(
            normalizedLogin,
            normalizedLogin.ToUpperInvariant(),
            normalizedName,
            normalizedEmail,
            normalizedEmail.ToUpperInvariant());
        error = null;
        return true;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            int atIndex = email.LastIndexOf('@');
            return email.Length > 0
                && atIndex > 0
                && atIndex < email.Length - 1
                && email[(atIndex + 1)..].Contains('.', StringComparison.Ordinal)
                && string.Equals(new MailAddress(email).Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task RevokeUserSessionsAsync(
        KapeldinerServiceDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        AuthSession[] sessions = await dbContext.AuthSessions.ToArrayAsync(cancellationToken);
        foreach (AuthSession session in sessions)
        {
            ClaimsPrincipal? principal = TicketSerializer.Default
                .Deserialize(Convert.FromBase64String(session.Ticket))
                ?.Principal;
            if (Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid sessionUserId)
                && sessionUserId == userId)
            {
                dbContext.AuthSessions.Remove(session);
            }
        }
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static UserResponse ToResponse(AuthUser user) =>
        new(user.Id, user.Login, user.Email, user.Name, user.Role, user.CreatedAtUtc);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User creation failed because SMTP email settings are missing.")]
    private static partial void LogEmailNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User creation failed because credential email delivery failed.")]
    private static partial void LogEmailDeliveryFailed(ILogger logger);

    public sealed record UserProfileRequest(string? Login, string? Email, string? Name);

    public sealed record UserResponse(Guid Id, string Login, string Email, string Name, string Role, DateTime CreatedAtUtc);

    public sealed record ProfileUpdateResponse(UserResponse Profile, bool SessionsRevoked);

    private sealed record NormalizedUserData(
        string Login,
        string NormalizedLogin,
        string Name,
        string Email,
        string NormalizedEmail);
}
