using KapeldinerService.Domain.Reservations;
using KapeldinerService.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KapeldinerService.Infrastructure.Postgres.Configurations;

public class ReservationSeatConfiguration : IEntityTypeConfiguration<ReservationSeat>
{
    public void Configure(EntityTypeBuilder<ReservationSeat> builder)
    {
        builder.ToTable("reservation_seats");

        builder.HasKey(rs => rs.Id).HasName("pk_reservation_seats");

        builder
            .Property(rs => rs.Id)
            .HasConversion(rs => rs.Value, rs => new ReservationSeatId(rs))
            .HasColumnName("id");

        builder
            .Property(rs => rs.ReservationId)
            .HasConversion(r => r.Value, r => new ReservationId(r))
            .HasColumnName("reservation_id");

        builder
            .Property(rs => rs.EventItemId)
            .HasConversion(eventItemId => eventItemId.Value, value => new EventItemId(value))
            .HasColumnName("event_item_id");

        builder.ComplexProperty(rs => rs.Seat, nb =>
        {
            nb
            .Property(s => s.RowLabel)
            .HasMaxLength(3)
            .HasColumnName("row_label");

            nb
            .Property(s => s.SeatNumber)
            .HasMaxLength(Seat.MaxSeats)
            .HasColumnName("seat_number");

            nb.Property(s => s.Status)
                .HasColumnName("status");

        });

        builder
            .Property(rs => rs.ReservedAt)
            .HasColumnName("reserved_at");

        builder.Property(rs => rs.SeatKey)
            .HasComputedColumnSql("row_label || '-' || seat_number", stored: true)
            .HasColumnName("seat_key");

        builder.HasOne(rs => rs.Reservation)
            .WithMany(reservation => reservation.ReservationSeats)
            .HasForeignKey(rs => new { rs.ReservationId, rs.EventItemId })
            .HasPrincipalKey(reservation => new { reservation.Id, reservation.EventItemId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex("EventItemId", "SeatKey")
            .IsUnique()
            .HasDatabaseName("ux_reservation_seat_event_seat");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_reservation_seat_location",
            """row_label IN ('1','2','3','4','5','6','7','8','9','10','11','12','13','14','15','16','17','18','19','20','21','22','23','24','25','26','27','12А') AND seat_number BETWEEN 1 AND 26"""));
    }
}
