using KapeldinerService.Infrastructure.Postgres.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KapeldinerService.Infrastructure.Postgres.Configurations;

public sealed class AuthUserConfiguration : IEntityTypeConfiguration<AuthUser>
{
    public void Configure(EntityTypeBuilder<AuthUser> builder)
    {
        builder.ToTable("auth_user");
        builder.HasKey(user => user.Id).HasName("pk_auth_user");

        builder.Property(user => user.Id).HasColumnName("id");
        builder.Property(user => user.Login).HasColumnName("login").HasMaxLength(50).IsRequired();
        builder.Property(user => user.NormalizedLogin).HasColumnName("normalized_login").HasMaxLength(50).IsRequired();
        builder.Property(user => user.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(254).IsRequired();
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(512).IsRequired();
        builder.Property(user => user.Role).HasColumnName("role").HasMaxLength(32).IsRequired();
        builder.Property(user => user.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(user => user.NormalizedLogin).IsUnique().HasDatabaseName("ux_auth_user_normalized_login");
        builder.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("ux_auth_user_normalized_email");
        builder.HasIndex(user => user.Role)
            .IsUnique()
            .HasFilter("\"role\" = 'Administrator'")
            .HasDatabaseName("ux_auth_user_single_administrator");
    }
}
