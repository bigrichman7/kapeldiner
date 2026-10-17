using Microsoft.EntityFrameworkCore;
using KapeldinerService.Infrastructure.Postgres.Authentication;

namespace KapeldinerService.Infrastructure.Postgres;

public class KapeldinerServiceDbContext : DbContext
{
    public KapeldinerServiceDbContext(DbContextOptions<KapeldinerServiceDbContext> options) 
        : base(options)
    {

    }

    public DbSet<AuthUser> AuthUsers => Set<AuthUser>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KapeldinerServiceDbContext).Assembly);
    }
}
