using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaLlaveYLaIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "subscription_invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_product_id_idempotency_key",
                table: "subscription_invoices",
                columns: new[] { "product_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscription_invoices_product_id_idempotency_key",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "subscription_invoices");
        }
    }
}
