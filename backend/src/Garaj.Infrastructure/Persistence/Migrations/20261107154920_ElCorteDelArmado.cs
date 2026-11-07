using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ElCorteDelArmado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "billing_settlement_id",
                table: "motorcycle_assemblies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pay_settlement_id",
                table: "motorcycle_assemblies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assembly_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    contractor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    technician_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assembly_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_assembly_settlements_assembly_contractors_contractor_id",
                        column: x => x.contractor_id,
                        principalTable: "assembly_contractors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_motorcycle_assemblies_tenant_id_billing_settlement_id",
                table: "motorcycle_assemblies",
                columns: new[] { "tenant_id", "billing_settlement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_motorcycle_assemblies_tenant_id_pay_settlement_id",
                table: "motorcycle_assemblies",
                columns: new[] { "tenant_id", "pay_settlement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assembly_settlements_contractor_id",
                table: "assembly_settlements",
                column: "contractor_id");

            migrationBuilder.CreateIndex(
                name: "ix_assembly_settlements_tenant_id_kind_period_to",
                table: "assembly_settlements",
                columns: new[] { "tenant_id", "kind", "period_to" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assembly_settlements");

            migrationBuilder.DropIndex(
                name: "ix_motorcycle_assemblies_tenant_id_billing_settlement_id",
                table: "motorcycle_assemblies");

            migrationBuilder.DropIndex(
                name: "ix_motorcycle_assemblies_tenant_id_pay_settlement_id",
                table: "motorcycle_assemblies");

            migrationBuilder.DropColumn(
                name: "billing_settlement_id",
                table: "motorcycle_assemblies");

            migrationBuilder.DropColumn(
                name: "pay_settlement_id",
                table: "motorcycle_assemblies");
        }
    }
}
