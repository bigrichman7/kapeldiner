using Microsoft.EntityFrameworkCore;
using KapeldinerService.Domain.Events;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using KapeldinerService.Domain.Reservations;

namespace KapeldinerService.Infrastructure.Postgres.Configurations;

public class EventQuotaConfiguration : IEntityTypeConfiguration<EventQuota>
{
    public void Configure(EntityTypeBuilder<EventQuota> builder)
    {
        builder.ToTable("event_quota");

        builder.HasKey(eq => eq.Id).HasName("pk_event_quotas");

        builder
            .Property(eq => eq.Id)
            .HasConversion(v => v.Value, v => new EventQuotaId(v))
            .HasColumnName("id");

        builder
            .Property(eq => eq.EventItemId)
            .HasConversion(v => v.Value, v => new EventItemId(v))
            .HasColumnName("event_item_id");

        builder
            .Property(eq => eq.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder
            .Property(eq => eq.AvailableSeats)
            .HasColumnName("available_seats")
            .IsRequired();

        builder
            .Property(eq => eq.UsedSeats)
            .HasColumnName("used_seats")
            .IsRequired();

        builder
            .HasOne(eq => eq.EventItem)
            .WithMany(ei => ei.EventQuotas)
            .HasForeignKey(eq => eq.EventItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(eq => new { eq.EventItemId, eq.UserId })
            .IsUnique()
            .HasDatabaseName("ux_event_quota_event_user");
    }
}
