using KapeldinerService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KapeldinerService.Infrastructure.Postgres.Migrations;

[DbContext(typeof(KapeldinerServiceDbContext))]
[Migration("20261017140000_AddUserProfile")]
public sealed class AddUserProfile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "name",
            table: "auth_user",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "email",
            table: "auth_user",
            type: "character varying(254)",
            maxLength: 254,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "normalized_email",
            table: "auth_user",
            type: "character varying(254)",
            maxLength: 254,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE auth_user
            SET name = login,
                email = lower(login) || '@invalid.local',
                normalized_email = upper(lower(login) || '@invalid.local');
            """);

        migrationBuilder.AlterColumn<string>(
            name: "name",
            table: "auth_user",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "email",
            table: "auth_user",
            type: "character varying(254)",
            maxLength: 254,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(254)",
            oldMaxLength: 254,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "normalized_email",
            table: "auth_user",
            type: "character varying(254)",
            maxLength: 254,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(254)",
            oldMaxLength: 254,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_auth_user_normalized_email",
            table: "auth_user",
            column: "normalized_email",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ux_auth_user_normalized_email", table: "auth_user");
        migrationBuilder.DropColumn(name: "email", table: "auth_user");
        migrationBuilder.DropColumn(name: "name", table: "auth_user");
        migrationBuilder.DropColumn(name: "normalized_email", table: "auth_user");
    }
}
