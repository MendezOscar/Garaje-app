using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BodegajeYAvisoDeListo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ready_notified_at",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "charges_storage",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "storage_daily_rate",
                table: "tenants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "storage_free_days",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ready_notified_at",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "charges_storage",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "storage_daily_rate",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "storage_free_days",
                table: "tenants");
        }
    }
}
