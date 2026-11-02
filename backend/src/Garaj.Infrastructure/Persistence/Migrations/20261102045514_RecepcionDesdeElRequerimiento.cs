using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecepcionDesdeElRequerimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vehicle_receptions_work_order_id",
                table: "vehicle_receptions");

            migrationBuilder.AlterColumn<Guid>(
                name: "work_order_id",
                table: "vehicle_receptions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "service_request_id",
                table: "vehicle_receptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_receptions_service_request_id",
                table: "vehicle_receptions",
                column: "service_request_id",
                unique: true,
                filter: "service_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_receptions_work_order_id",
                table: "vehicle_receptions",
                column: "work_order_id",
                unique: true,
                filter: "work_order_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_vehicle_receptions_service_requests_service_request_id",
                table: "vehicle_receptions",
                column: "service_request_id",
                principalTable: "service_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_vehicle_receptions_service_requests_service_request_id",
                table: "vehicle_receptions");

            migrationBuilder.DropIndex(
                name: "ix_vehicle_receptions_service_request_id",
                table: "vehicle_receptions");

            migrationBuilder.DropIndex(
                name: "ix_vehicle_receptions_work_order_id",
                table: "vehicle_receptions");

            migrationBuilder.DropColumn(
                name: "service_request_id",
                table: "vehicle_receptions");

            migrationBuilder.AlterColumn<Guid>(
                name: "work_order_id",
                table: "vehicle_receptions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_receptions_work_order_id",
                table: "vehicle_receptions",
                column: "work_order_id",
                unique: true);
        }
    }
}
