using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KapeldinerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EventHistoryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_event_item_end_at",
                table: "event_item",
                column: "end_at");

            migrationBuilder.CreateIndex(
                name: "ix_event_item_start_at",
                table: "event_item",
                column: "start_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_event_item_end_at",
                table: "event_item");

            migrationBuilder.DropIndex(
                name: "ix_event_item_start_at",
                table: "event_item");
        }
    }
}
