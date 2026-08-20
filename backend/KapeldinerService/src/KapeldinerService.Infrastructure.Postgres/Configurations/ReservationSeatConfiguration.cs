using KapeldinerService.Domain.Reservations;
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

        builder.ComplexProperty(rs => rs.Seat, nb =>
        {
            nb
            .Property(s => s.RowNumber)
            .HasMaxLength(Seat.MaxRows)
            .HasColumnName("row_number");

            nb
            .Property(s => s.SeatNumber)
            .HasMaxLength(Seat.MaxSeats)
            .HasColumnName("seat_number");

            nb
            .Property(s => s.Status)
            .HasColumnName("status");
        });

        builder
            .Property(rs => rs.ReservedAt)
            .HasColumnName("reserved_at");
    }
}

