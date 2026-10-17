using KapeldinerService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KapeldinerService.Infrastructure.Postgres.Migrations;

[DbContext(typeof(KapeldinerServiceDbContext))]
[Migration("20261017120000_AddAuthentication")]
public sealed class AddAuthentication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "auth_session",
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ticket = table.Column<string>(type: "text", nullable: false),
                expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_auth_session", session => session.id));

        migrationBuilder.CreateTable(
            name: "auth_user",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                login = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                normalized_login = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_auth_user", user => user.id));

        migrationBuilder.CreateIndex(
            name: "ix_auth_session_expires_at_utc",
            table: "auth_session",
            column: "expires_at_utc");

        migrationBuilder.CreateIndex(
            name: "ux_auth_user_normalized_login",
            table: "auth_user",
            column: "normalized_login",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_auth_user_single_administrator",
            table: "auth_user",
            column: "role",
            unique: true,
            filter: "\"role\" = 'Administrator'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "auth_session");
        migrationBuilder.DropTable(name: "auth_user");
    }
}
