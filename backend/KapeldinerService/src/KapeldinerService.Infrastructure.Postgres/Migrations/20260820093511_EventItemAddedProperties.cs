using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KapeldinerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EventItemAddedProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "event_item",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "event_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "event_item");

            migrationBuilder.DropColumn(
                name: "status",
                table: "event_item");
        }
    }
}
