using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Suscripciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "grace_days",
                table: "tenants",
                type: "integer",
                nullable: false,
                // 5 y no 0: los talleres que ya existen se quedarían sin ningún día de
                // tolerancia el día que se les ponga fecha de pago.
                defaultValue: 5);

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_fee",
                table: "tenants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "paid_through",
                table: "tenants",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_name",
                table: "tenants",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unblock_note",
                table: "tenants",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "unblocked_through",
                table: "tenants",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscription_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    covers_through = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    registered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_payments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_paid_through",
                table: "tenants",
                column: "paid_through");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_tenant_id_paid_on",
                table: "subscription_payments",
                columns: new[] { "tenant_id", "paid_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_payments");

            migrationBuilder.DropIndex(
                name: "ix_tenants_paid_through",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "grace_days",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "monthly_fee",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "paid_through",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "plan_name",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "unblock_note",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "unblocked_through",
                table: "tenants");
        }
    }
}
