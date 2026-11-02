using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AvisosDelCalendario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dedupe_key",
                table: "notifications",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_user_id_dedupe_key",
                table: "notifications",
                columns: new[] { "recipient_user_id", "dedupe_key" },
                unique: true,
                filter: "dedupe_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_recipient_user_id_dedupe_key",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "dedupe_key",
                table: "notifications");
        }
    }
}
