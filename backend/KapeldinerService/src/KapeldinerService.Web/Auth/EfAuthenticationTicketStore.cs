using System.Security.Cryptography;
using KapeldinerService.Infrastructure.Postgres;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Web.Auth;

public sealed class EfAuthenticationTicketStore(IDbContextFactory<KapeldinerServiceDbContext> contextFactory) : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        string id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        DateTime expiresAtUtc = GetExpiresAtUtc(ticket);

        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync();
        dbContext.AuthSessions.Add(new AuthSession
        {
            Id = id,
            Ticket = TicketSerializer.Default.Serialize(ticket),
            ExpiresAtUtc = expiresAtUtc
        });
        await dbContext.SaveChangesAsync();

        return id;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync();
        AuthSession? session = await dbContext.AuthSessions.FindAsync(key);
        if (session is null)
        {
            return;
        }

        session.Ticket = TicketSerializer.Default.Serialize(ticket);
        session.ExpiresAtUtc = GetExpiresAtUtc(ticket);
        await dbContext.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync();
        AuthSession? session = await dbContext.AuthSessions.FindAsync(key);
        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAtUtc <= DateTime.UtcNow)
        {
            dbContext.AuthSessions.Remove(session);
            await dbContext.SaveChangesAsync();
            return null;
        }

        return TicketSerializer.Default.Deserialize(session.Ticket);
    }

    public async Task RemoveAsync(string key)
    {
        await using KapeldinerServiceDbContext dbContext = await contextFactory.CreateDbContextAsync();
        AuthSession? session = await dbContext.AuthSessions.FindAsync(key);
        if (session is null)
        {
            return;
        }

        dbContext.AuthSessions.Remove(session);
        await dbContext.SaveChangesAsync();
    }

    private static DateTime GetExpiresAtUtc(AuthenticationTicket ticket)
    {
        return ticket.Properties.ExpiresUtc?.UtcDateTime ?? DateTime.UtcNow.AddHours(8);
    }
}
