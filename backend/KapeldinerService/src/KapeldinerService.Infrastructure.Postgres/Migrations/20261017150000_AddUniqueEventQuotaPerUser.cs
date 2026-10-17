using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KapeldinerService.Infrastructure.Postgres.Migrations
{
    /// <summary>Enforces one quota per event and user.</summary>
    public partial class AddUniqueEventQuotaPerUser : Migration
    {
        private static readonly string[] EventQuotaIndexColumns = ["event_item_id", "user_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_event_quota_event_item_id",
                table: "event_quota");

            migrationBuilder.CreateIndex(
                name: "ux_event_quota_event_user",
                table: "event_quota",
                columns: EventQuotaIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_event_quota_event_user",
                table: "event_quota");

            migrationBuilder.CreateIndex(
                name: "IX_event_quota_event_item_id",
                table: "event_quota",
                column: "event_item_id");
        }
    }
}
