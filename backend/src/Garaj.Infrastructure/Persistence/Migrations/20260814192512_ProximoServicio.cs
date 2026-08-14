using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProximoServicio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_service_at",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "next_service_mileage",
                table: "work_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_service_reminded_at",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_next_service_at",
                table: "work_orders",
                columns: new[] { "tenant_id", "next_service_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_orders_tenant_id_next_service_at",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "next_service_at",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "next_service_mileage",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "next_service_reminded_at",
                table: "work_orders");
        }
    }
}
