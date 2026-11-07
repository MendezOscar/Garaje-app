using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ElCimientoDelArmado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "assembly_enabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "assembly_contractors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assembly_contractors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assembly_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assembly_models", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assembly_warehouses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assembly_warehouses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assembly_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contractor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    technician_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assembly_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_assembly_rates_assembly_contractors_contractor_id",
                        column: x => x.contractor_id,
                        principalTable: "assembly_contractors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assembly_rates_assembly_models_model_id",
                        column: x => x.model_id,
                        principalTable: "assembly_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "motorcycle_assemblies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contractor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assembled_on = table.Column<DateOnly>(type: "date", nullable: false),
                    serial = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    charge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    technician_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_motorcycle_assemblies", x => x.id);
                    table.ForeignKey(
                        name: "fk_motorcycle_assemblies_assembly_contractors_contractor_id",
                        column: x => x.contractor_id,
                        principalTable: "assembly_contractors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_motorcycle_assemblies_assembly_models_model_id",
                        column: x => x.model_id,
                        principalTable: "assembly_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_motorcycle_assemblies_assembly_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "assembly_warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_motorcycle_assemblies_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assembly_contractors_tenant_id_name",
                table: "assembly_contractors",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assembly_models_tenant_id_name",
                table: "assembly_models",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assembly_rates_contractor_id",
                table: "assembly_rates",
                column: "contractor_id");

            migrationBuilder.CreateIndex(
                name: "ix_assembly_rates_model_id",
                table: "assembly_rates",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_assembly_rates_tenant_id_contractor_id_model_id",
                table: "assembly_rates",
                columns: new[] { "tenant_id", "contractor_id", "model_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assembly_warehouses_tenant_id_name",
                table: "assembly_warehouses",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_motorcycle_assemblies_branch_id",
                table: "motorcycle_assemblies",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_motorcycle_assemblies_contractor_id",
                table: "motorcycle_assemblies",
                column: "contractor_id");

            migrationBuilder.CreateIndex(
                name: "ix_motorcycle_assemblies_model_id",
                table: "motorcycle_assemblies",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_motorcycle_assemblies_tenant_id_assembled_on",
                table: "motorcycle_assemblies",
                columns: new[] { "tenant_id", "assembled_on" });

            migrationBuilder.CreateIndex(
                name: "ix_motorcycle_assemblies_tenant_id_serial",
                table: "motorcycle_assemblies",
                columns: new[] { "tenant_id", "serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_motorcycle_assemblies_tenant_id_technician_user_id_assemble",
                table: "motorcycle_assemblies",
                columns: new[] { "tenant_id", "technician_user_id", "assembled_on" });

            migrationBuilder.CreateIndex(
                name: "ix_motorcycle_assemblies_warehouse_id",
                table: "motorcycle_assemblies",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assembly_rates");

            migrationBuilder.DropTable(
                name: "motorcycle_assemblies");

            migrationBuilder.DropTable(
                name: "assembly_contractors");

            migrationBuilder.DropTable(
                name: "assembly_models");

            migrationBuilder.DropTable(
                name: "assembly_warehouses");

            migrationBuilder.DropColumn(
                name: "assembly_enabled",
                table: "tenants");
        }
    }
}
