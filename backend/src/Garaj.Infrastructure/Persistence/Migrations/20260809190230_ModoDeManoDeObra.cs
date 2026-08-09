using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModoDeManoDeObra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "labor_price",
                table: "work_order_tasks");

            // 1 = LaborMode.Catalog, que es como venían funcionando las órdenes existentes.
            // El scaffolding proponía 0, que no es ningún modo.
            migrationBuilder.AddColumn<int>(
                name: "labor_mode",
                table: "work_orders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "manual_labor_total",
                table: "work_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "labor_mode",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "manual_labor_total",
                table: "work_orders");

            migrationBuilder.AddColumn<decimal>(
                name: "labor_price",
                table: "work_order_tasks",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }
    }
}
