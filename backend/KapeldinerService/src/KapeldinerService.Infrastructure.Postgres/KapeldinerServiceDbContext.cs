using Microsoft.EntityFrameworkCore;

namespace KapeldinerService.Infrastructure.Postgres;

public class KapeldinerServiceDbContext : DbContext
{
    public KapeldinerServiceDbContext(DbContextOptions<KapeldinerServiceDbContext> options) 
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KapeldinerServiceDbContext).Assembly);
    }
}

