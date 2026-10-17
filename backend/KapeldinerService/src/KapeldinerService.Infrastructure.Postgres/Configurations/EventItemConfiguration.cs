using KapeldinerService.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KapeldinerService.Infrastructure.Postgres.Configurations;

public class EventItemConfiguration : IEntityTypeConfiguration<EventItem>
{
    public void Configure(EntityTypeBuilder<EventItem> builder)
    {
        builder.ToTable("event_item");

        builder.HasKey(ei => ei.Id).HasName("pk_event_items");

        builder
            .Property(ei => ei.Id)
            .HasConversion(v => v.Value, v => new EventItemId(v))
            .HasColumnName("id");

        builder
            .Property(ei => ei.Title)
            .IsRequired()
            .HasColumnName ("title");

        builder
            .Property(ei => ei.Description)
            .HasColumnName("description");

        builder
            .Property(ei => ei.StardAt)
            .IsRequired()
            .HasColumnName("start_at");

        builder
            .Property(ei => ei.EndAt)
            .IsRequired()
            .HasColumnName("end_at");

        builder
            .Property(ei => ei.Status)
            .IsRequired()
            .HasColumnName("status");

        builder
            .Property(ei => ei.CreatedAt)
            .HasColumnName("created_at");

        builder
            .Property(ei => ei.EnableAdditionalRow12A)
            .IsRequired()
            .HasColumnName("enable_additional_row_12a");

        builder.HasIndex(ei => ei.StardAt).HasDatabaseName("ix_event_item_start_at");
        builder.HasIndex(ei => ei.EndAt).HasDatabaseName("ix_event_item_end_at");
    }
}
