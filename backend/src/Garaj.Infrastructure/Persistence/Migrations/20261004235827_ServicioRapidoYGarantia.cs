using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicioRapidoYGarantia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "default_warranty_days",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "vehicle_id",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "warranty_days",
                table: "sales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "warranty_until",
                table: "sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_vehicle_id",
                table: "sales",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_vehicle_id",
                table: "quotes",
                column: "vehicle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_vehicle_id",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_quotes_vehicle_id",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "default_warranty_days",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "vehicle_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "warranty_days",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "warranty_until",
                table: "sales");
        }
    }
}
