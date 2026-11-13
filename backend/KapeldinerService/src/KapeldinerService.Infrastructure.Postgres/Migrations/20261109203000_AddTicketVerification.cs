using KapeldinerService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KapeldinerService.Infrastructure.Postgres.Migrations;

[DbContext(typeof(KapeldinerServiceDbContext))]
[Migration("20261109203000_AddTicketVerification")]
public sealed class AddTicketVerification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "verification_token",
            table: "reservation_seats",
            type: "character varying(43)",
            maxLength: 43,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE reservation_seats
            SET verification_token = gen_random_uuid()::text
            WHERE verification_token IS NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "verification_token",
            table: "reservation_seats",
            type: "character varying(43)",
            maxLength: 43,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(43)",
            oldMaxLength: 43,
            oldNullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "verified_at_utc",
            table: "reservation_seats",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_reservation_seat_verification_token",
            table: "reservation_seats",
            column: "verification_token",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_reservation_seat_verification_token",
            table: "reservation_seats");

        migrationBuilder.DropColumn(
            name: "verification_token",
            table: "reservation_seats");

        migrationBuilder.DropColumn(
            name: "verified_at_utc",
            table: "reservation_seats");
    }
}
