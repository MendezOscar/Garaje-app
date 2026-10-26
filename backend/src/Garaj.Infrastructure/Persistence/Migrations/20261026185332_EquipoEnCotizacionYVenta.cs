using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EquipoEnCotizacionYVenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "equipment_id",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "equipment_id",
                table: "quotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_equipment_id",
                table: "sales",
                column: "equipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_equipment_id",
                table: "quotes",
                column: "equipment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_equipment_id",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_quotes_equipment_id",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "equipment_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "equipment_id",
                table: "quotes");
        }
    }
}
