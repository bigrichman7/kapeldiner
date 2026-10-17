namespace KapeldinerService.Infrastructure.Postgres.Authentication;

public sealed class AuthUser
{
    public Guid Id { get; set; }

    public required string Login { get; set; }

    public required string NormalizedLogin { get; set; }

    public string Name { get; set; } = "Administrator";

    public string Email { get; set; } = "administrator@invalid.local";

    public string NormalizedEmail { get; set; } = "ADMINISTRATOR@INVALID.LOCAL";

    public required string PasswordHash { get; set; }

    public required string Role { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
