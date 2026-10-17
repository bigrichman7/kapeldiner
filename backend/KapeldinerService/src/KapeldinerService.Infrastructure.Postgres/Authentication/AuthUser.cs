namespace KapeldinerService.Infrastructure.Postgres.Authentication;

public sealed class AuthUser
{
    public Guid Id { get; set; }

    public required string Login { get; set; }

    public required string NormalizedLogin { get; set; }

    public required string PasswordHash { get; set; }

    public required string Role { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
