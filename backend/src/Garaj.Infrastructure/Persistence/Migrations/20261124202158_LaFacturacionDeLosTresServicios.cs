using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaFacturacionDeLosTresServicios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                table: "subscription_invoices",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "subscription_payment_id",
                table: "subscription_invoices",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "external_customer_ref",
                table: "subscription_invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_reference",
                table: "subscription_invoices",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "product_id",
                table: "subscription_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_name",
                table: "subscription_invoices",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "platform_products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    api_key_hint = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    api_key_issued_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_products", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_product_id_external_customer_ref",
                table: "subscription_invoices",
                columns: new[] { "product_id", "external_customer_ref" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_product_id_issued_on",
                table: "subscription_invoices",
                columns: new[] { "product_id", "issued_on" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_products_name",
                table: "platform_products",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_invoices_platform_products_product_id",
                table: "subscription_invoices",
                column: "product_id",
                principalTable: "platform_products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Los tres servicios que ya se cobran. Con id fijo porque la factura vieja tiene
            // que poder apuntar a GarajApp en esta misma migración.
            migrationBuilder.Sql(
                "INSERT INTO platform_products (id, name, is_active, created_at) VALUES "
                + "('1a5d1f00-0000-4000-8000-000000000001', 'GarajApp', true, now()), "
                + "('1a5d1f00-0000-4000-8000-000000000002', 'StockRuta', true, now()), "
                + "('1a5d1f00-0000-4000-8000-000000000003', 'Profeapp', true, now()) "
                + "ON CONFLICT (name) DO NOTHING;");

            // Lo ya emitido es de GarajApp. Dejarlo sin servicio lo escondería de la pestaña
            // de documentos, que es justo donde hay que poder encontrarlo.
            migrationBuilder.Sql(
                "UPDATE subscription_invoices SET "
                + "product_id = '1a5d1f00-0000-4000-8000-000000000001', "
                + "product_name = 'GarajApp' "
                + "WHERE product_id IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE subscription_invoices SET product_id = NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_subscription_invoices_platform_products_product_id",
                table: "subscription_invoices");

            migrationBuilder.DropTable(
                name: "platform_products");

            migrationBuilder.DropIndex(
                name: "ix_subscription_invoices_product_id_external_customer_ref",
                table: "subscription_invoices");

            migrationBuilder.DropIndex(
                name: "ix_subscription_invoices_product_id_issued_on",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "external_customer_ref",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "payment_reference",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "product_id",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "product_name",
                table: "subscription_invoices");

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                table: "subscription_invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "subscription_payment_id",
                table: "subscription_invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
