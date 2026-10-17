using KapeldinerService.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KapeldinerService.Infrastructure.Postgres.Configurations;

public sealed class EventSeatDesignationConfiguration : IEntityTypeConfiguration<EventSeatDesignation>
{
    public void Configure(EntityTypeBuilder<EventSeatDesignation> builder)
    {
        builder.ToTable("event_seat_designation", table =>
        {
            table.HasCheckConstraint("ck_event_seat_designation_type", "designation IN (0, 1)");
            table.HasCheckConstraint(
                "ck_event_seat_designation_location",
                """row_label IN ('1','2','3','4','5','6','7','8','9','10','11','12','13','14','15','16','17','18','19','20','21','22','23','24','25','26','27','12А') AND seat_number BETWEEN 1 AND 26""");
        });

        builder.HasKey(seat => new { seat.EventItemId, seat.RowLabel, seat.SeatNumber })
            .HasName("pk_event_seat_designation");

        builder.Property(seat => seat.EventItemId)
            .HasConversion(eventItemId => eventItemId.Value, value => new EventItemId(value))
            .HasColumnName("event_item_id");
        builder.Property(seat => seat.RowLabel)
            .HasMaxLength(3)
            .HasColumnName("row_label");
        builder.Property(seat => seat.SeatNumber)
            .HasColumnName("seat_number");
        builder.Property(seat => seat.Designation)
            .HasColumnName("designation");

        builder.HasOne(seat => seat.EventItem)
            .WithMany(eventItem => eventItem.SeatDesignations)
            .HasForeignKey(seat => seat.EventItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
