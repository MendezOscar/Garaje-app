using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepuestoDeCasaDeRepuestos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "bought_outside",
                table: "work_order_parts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "supplier_name",
                table: "work_order_parts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            // `xmin` no se agrega: en PostgreSQL es una columna de sistema que ya existe en
            // toda tabla. EF la pide porque el modelo la declara como token de concurrencia,
            // pero un ALTER TABLE ADD COLUMN xmin falla y dejaría la API sin arrancar.

            migrationBuilder.AddColumn<bool>(
                name: "bought_outside",
                table: "quote_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "supplier_name",
                table: "quote_lines",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bought_outside",
                table: "work_order_parts");

            migrationBuilder.DropColumn(
                name: "supplier_name",
                table: "work_order_parts");

            migrationBuilder.DropColumn(
                name: "bought_outside",
                table: "quote_lines");

            migrationBuilder.DropColumn(
                name: "supplier_name",
                table: "quote_lines");
        }
    }
}
