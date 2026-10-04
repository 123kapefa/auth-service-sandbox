using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class LoginDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "login_devices",
                schema: "auth",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_devices", x => x.session_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_login_devices_created_at",
                schema: "auth",
                table: "login_devices",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_login_devices_user_id",
                schema: "auth",
                table: "login_devices",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_devices",
                schema: "auth");
        }
    }
}
