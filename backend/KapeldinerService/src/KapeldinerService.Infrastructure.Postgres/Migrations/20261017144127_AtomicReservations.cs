using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KapeldinerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AtomicReservations : Migration
    {
        private static readonly string[] ReservationColumns = ["reservation_id", "event_item_id"];
        private static readonly string[] ReservationPrimaryColumns = ["id", "event_item_id"];
        private static readonly string[] EventSeatColumns = ["event_item_id", "seat_key"];
        private const string SeatLocationConstraint =
            "row_label IN ('1','2','3','4','5','6','7','8','9','10','11','12','13','14','15','16','17','18','19','20','21','22','23','24','25','26','27','12А') AND seat_number BETWEEN 1 AND 26";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reservation_seats_reservation_reservation_id",
                table: "reservation_seats");

            migrationBuilder.DropIndex(
                name: "IX_reservation_seats_reservation_id",
                table: "reservation_seats");

            migrationBuilder.AddColumn<Guid>(
                name: "event_item_id",
                table: "reservation_seats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "row_label",
                table: "reservation_seats",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE reservation_seats AS seat
                SET event_item_id = reservation.event_item_id,
                    row_label = seat.row_number::text
                FROM reservation
                WHERE reservation.id = seat.reservation_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "event_item_id",
                table: "reservation_seats",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "row_label",
                table: "reservation_seats",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "row_number",
                table: "reservation_seats");

            migrationBuilder.AddColumn<bool>(
                name: "enable_additional_row_12a",
                table: "event_item",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "seat_key",
                table: "reservation_seats",
                type: "text",
                nullable: false,
                computedColumnSql: "row_label || '-' || seat_number",
                stored: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_reservation_id_event_item",
                table: "reservation",
                columns: ReservationPrimaryColumns);

            migrationBuilder.CreateTable(
                name: "event_seat_designation",
                columns: table => new
                {
                    event_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_label = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    seat_number = table.Column<int>(type: "integer", nullable: false),
                    designation = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_seat_designation", x => new { x.event_item_id, x.row_label, x.seat_number });
                    table.CheckConstraint("ck_event_seat_designation_location", SeatLocationConstraint);
                    table.CheckConstraint("ck_event_seat_designation_type", "designation IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_event_seat_designation_event_item_event_item_id",
                        column: x => x.event_item_id,
                        principalTable: "event_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reservation_seats_reservation_id_event_item_id",
                table: "reservation_seats",
                columns: ReservationColumns);

            migrationBuilder.CreateIndex(
                name: "ux_reservation_seat_event_seat",
                table: "reservation_seats",
                columns: EventSeatColumns,
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservation_seat_location",
                table: "reservation_seats",
                sql: SeatLocationConstraint);

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_seats_reservation_reservation_id_event_item_id",
                table: "reservation_seats",
                columns: ReservationColumns,
                principalTable: "reservation",
                principalColumns: ReservationPrimaryColumns,
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM reservation_seats WHERE row_label = '12А') THEN
                        RAISE EXCEPTION 'Cannot revert the row-label migration while 12А reservations exist.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservation_seat_location",
                table: "reservation_seats");

            migrationBuilder.DropForeignKey(
                name: "FK_reservation_seats_reservation_reservation_id_event_item_id",
                table: "reservation_seats");

            migrationBuilder.DropTable(
                name: "event_seat_designation");

            migrationBuilder.DropIndex(
                name: "IX_reservation_seats_reservation_id_event_item_id",
                table: "reservation_seats");

            migrationBuilder.DropIndex(
                name: "ux_reservation_seat_event_seat",
                table: "reservation_seats");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_reservation_id_event_item",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "seat_key",
                table: "reservation_seats");

            migrationBuilder.DropColumn(
                name: "event_item_id",
                table: "reservation_seats");

            migrationBuilder.DropColumn(
                name: "row_label",
                table: "reservation_seats");

            migrationBuilder.DropColumn(
                name: "enable_additional_row_12a",
                table: "event_item");

            migrationBuilder.AddColumn<int>(
                name: "row_number",
                table: "reservation_seats",
                type: "integer",
                maxLength: 27,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE reservation_seats
                SET row_number = row_label::integer;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "row_number",
                table: "reservation_seats",
                type: "integer",
                maxLength: 27,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldMaxLength: 27,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservation_seats_reservation_id",
                table: "reservation_seats",
                column: "reservation_id");

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_seats_reservation_reservation_id",
                table: "reservation_seats",
                column: "reservation_id",
                principalTable: "reservation",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
