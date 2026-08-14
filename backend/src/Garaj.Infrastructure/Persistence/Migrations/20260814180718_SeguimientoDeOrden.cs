using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeguimientoDeOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "public_token",
                table: "work_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Un token por orden **antes** del índice único: el valor por omisión es el mismo
            // Guid vacío para todas las filas que ya existen, y el índice fallaría al crearse.
            migrationBuilder.Sql("UPDATE work_orders SET public_token = gen_random_uuid();");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_public_token",
                table: "work_orders",
                column: "public_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_orders_public_token",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "public_token",
                table: "work_orders");
        }
    }
}
