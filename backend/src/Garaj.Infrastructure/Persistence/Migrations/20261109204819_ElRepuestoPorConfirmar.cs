using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ElRepuestoPorConfirmar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "confirmed_by_user_id",
                table: "work_order_parts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmed_on",
                table: "work_order_parts",
                type: "timestamp with time zone",
                nullable: true);

            // Todo lo que ya estaba cargado SÍ salió de bodega: se descontó al cargarlo, que
            // era la única forma que había. Dejarlo en nulo lo volvería «por confirmar» y
            // bloquearía la entrega de órdenes abiertas —y confirmarlo descontaría la bodega
            // por segunda vez—.
            migrationBuilder.Sql(
                "UPDATE work_order_parts SET confirmed_on = created_at WHERE confirmed_on IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "confirmed_by_user_id",
                table: "work_order_parts");

            migrationBuilder.DropColumn(
                name: "confirmed_on",
                table: "work_order_parts");
        }
    }
}
