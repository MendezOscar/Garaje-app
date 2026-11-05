using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanesYTopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "diagnostics_enabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "plan",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "technician_limit",
                table: "tenants",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "diagnostics_enabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "plan",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "technician_limit",
                table: "tenants");
        }
    }
}
