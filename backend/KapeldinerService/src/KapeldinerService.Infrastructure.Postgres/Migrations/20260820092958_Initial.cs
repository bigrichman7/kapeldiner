using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KapeldinerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "event_quota",
                columns: table => new
                {
                    event_quota = table.Column<Guid>(type: "uuid", nullable: false),
                    event_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    available_seats = table.Column<int>(type: "integer", nullable: false),
                    used_seats = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_quotas", x => x.event_quota);
                    table.ForeignKey(
                        name: "FK_event_quota_event_item_event_item_id",
                        column: x => x.event_item_id,
                        principalTable: "event_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_quota = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservations", x => x.id);
                    table.ForeignKey(
                        name: "FK_reservation_event_item_event_item_id",
                        column: x => x.event_item_id,
                        principalTable: "event_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reservation_event_quota_event_quota",
                        column: x => x.event_quota,
                        principalTable: "event_quota",
                        principalColumn: "event_quota",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservation_seats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reserved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    row_number = table.Column<int>(type: "integer", maxLength: 27, nullable: false),
                    seat_number = table.Column<int>(type: "integer", maxLength: 25, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservation_seats", x => x.id);
                    table.ForeignKey(
                        name: "FK_reservation_seats_reservation_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "reservation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_quota_event_item_id",
                table: "event_quota",
                column: "event_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_event_item_id",
                table: "reservation",
                column: "event_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_event_quota",
                table: "reservation",
                column: "event_quota",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservation_seats_reservation_id",
                table: "reservation_seats",
                column: "reservation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reservation_seats");

            migrationBuilder.DropTable(
                name: "reservation");

            migrationBuilder.DropTable(
                name: "event_quota");

            migrationBuilder.DropTable(
                name: "event_item");
        }
    }
}
