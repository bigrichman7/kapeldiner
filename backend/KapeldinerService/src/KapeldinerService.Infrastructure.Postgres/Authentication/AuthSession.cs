namespace KapeldinerService.Infrastructure.Postgres.Authentication;

public sealed class AuthSession
{
    public required string Id { get; set; }

    public required string Ticket { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}
