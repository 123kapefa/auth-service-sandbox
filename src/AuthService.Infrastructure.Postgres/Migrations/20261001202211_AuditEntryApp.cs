using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AuditEntryApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "app",
                schema: "auth",
                table: "audit_entries",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_app_occurred_at",
                schema: "auth",
                table: "audit_entries",
                columns: new[] { "app", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_entries_app_occurred_at",
                schema: "auth",
                table: "audit_entries");

            migrationBuilder.DropColumn(
                name: "app",
                schema: "auth",
                table: "audit_entries");
        }
    }
}
