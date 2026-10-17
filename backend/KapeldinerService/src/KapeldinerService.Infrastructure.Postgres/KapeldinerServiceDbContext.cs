using Microsoft.EntityFrameworkCore;
using KapeldinerService.Infrastructure.Postgres.Authentication;
using KapeldinerService.Domain.Events;

namespace KapeldinerService.Infrastructure.Postgres;

public class KapeldinerServiceDbContext : DbContext
{
    public KapeldinerServiceDbContext(DbContextOptions<KapeldinerServiceDbContext> options) 
        : base(options)
    {

    }

    public DbSet<AuthUser> AuthUsers => Set<AuthUser>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<EventItem> EventItems => Set<EventItem>();

    public DbSet<EventQuota> EventQuotas => Set<EventQuota>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KapeldinerServiceDbContext).Assembly);
    }
}
