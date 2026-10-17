using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Web.Auth;

public sealed partial class AdminBootstrapper(
    IDbContextFactory<KapeldinerServiceDbContext> contextFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    IPasswordHasher<AuthUser> passwordHasher,
    ILogger<AdminBootstrapper> logger) : IHostedService
{
    private const string AdminConfigurationPath = "Auth:Bootstrap:Admin";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string? login = configuration[$"{AdminConfigurationPath}:Login"];
        string? password = configuration[$"{AdminConfigurationPath}:Password"];
        if (string.IsNullOrWhiteSpace(login) && string.IsNullOrWhiteSpace(password) && environment.IsDevelopment())
        {
            LogDevelopmentBootstrapDisabled(logger);
            return;
        }

        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (await dbContext.AuthUsers.AnyAsync(user => user.Role == AuthRoles.Administrator, cancellationToken))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"Configure both {AdminConfigurationPath}:Login and {AdminConfigurationPath}:Password to bootstrap the first administrator.");
        }

        login = login.Trim();
        if (login.Length is < 3 or > 50)
        {
            throw new InvalidOperationException("The configured administrator login must be between 3 and 50 characters.");
        }

        if (!environment.IsDevelopment() && password.Length < 12)
        {
            throw new InvalidOperationException("The production administrator bootstrap password must contain at least 12 characters.");
        }

        var admin = new AuthUser
        {
            Id = Guid.NewGuid(),
            Login = login,
            NormalizedLogin = login.ToUpperInvariant(),
            PasswordHash = string.Empty,
            Role = AuthRoles.Administrator,
            CreatedAtUtc = DateTime.UtcNow
        };
        admin.PasswordHash = passwordHasher.HashPassword(admin, password);
        dbContext.AuthUsers.Add(admin);
        await dbContext.SaveChangesAsync(cancellationToken);

        LogAdministratorBootstrapped(logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Development admin bootstrap is disabled because no credentials are configured.")]
    private static partial void LogDevelopmentBootstrapDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bootstrapped the initial administrator account.")]
    private static partial void LogAdministratorBootstrapped(ILogger logger);
}
