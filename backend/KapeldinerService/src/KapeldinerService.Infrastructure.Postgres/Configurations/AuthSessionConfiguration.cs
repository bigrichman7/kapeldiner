using KapeldinerService.Infrastructure.Postgres.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KapeldinerService.Infrastructure.Postgres.Configurations;

public sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_session");
        builder.HasKey(session => session.Id).HasName("pk_auth_session");

        builder.Property(session => session.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(session => session.Ticket).HasColumnName("ticket").IsRequired();
        builder.Property(session => session.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();

        builder.HasIndex(session => session.ExpiresAtUtc).HasDatabaseName("ix_auth_session_expires_at_utc");
    }
}
