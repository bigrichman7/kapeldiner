using Microsoft.EntityFrameworkCore;
using KapeldinerService.Domain.Reservations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using KapeldinerService.Domain.Events;

namespace KapeldinerService.Infrastructure.Postgres.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservation");

        builder.HasKey(r => r.Id).HasName("pk_reservations");

        builder
            .Property(r => r.Id)
            .HasConversion(r => r.Value, r => new ReservationId(r))
            .HasColumnName("id");

        builder
            .Property(r => r.UserId)
            .HasColumnName("user_id");

        builder
            .Property(r => r.EventItemId)
            .HasConversion(ei =>  ei.Value, ei => new EventItemId(ei))
            .HasColumnName("event_item_id");

        builder
            .Property(r => r.EventQuotaId)
            .HasConversion(eq =>  eq.Value, eq => new EventQuotaId(eq))
            .HasColumnName("event_quota");

        builder
            .HasOne(r => r.EventItem)
            .WithMany(ei => ei.Reservations)
            .HasForeignKey(r => r.EventItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(r => r.EventQuota)
            .WithOne(eq => eq.Reservation)
            .HasForeignKey<Reservation>(r => r.EventQuotaId)
            .IsRequired();

        builder.HasAlternateKey(r => new { r.Id, r.EventItemId })
            .HasName("ak_reservation_id_event_item");

    }
}
