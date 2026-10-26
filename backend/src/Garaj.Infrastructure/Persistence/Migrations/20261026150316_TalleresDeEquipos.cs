using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TalleresDeEquipos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "vehicle_id",
                table: "work_orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "equipment_id",
                table: "work_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "works_with",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<Guid>(
                name: "vehicle_id",
                table: "service_requests",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "equipment_id",
                table: "service_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "equipment_id",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "equipment_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipment_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "equipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipment_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    tag = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    color = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    accessories = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attributes = table.Column<string>(type: "jsonb", nullable: true),
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
                    table.PrimaryKey("pk_equipment", x => x.id);
                    table.ForeignKey(
                        name: "fk_equipment_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_equipment_equipment_types_equipment_type_id",
                        column: x => x.equipment_type_id,
                        principalTable: "equipment_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_equipment_id",
                table: "work_orders",
                column: "equipment_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_vehiculo_o_equipo",
                table: "work_orders",
                sql: "(vehicle_id IS NULL) <> (equipment_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_equipment_id",
                table: "service_requests",
                column: "equipment_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_requests_vehiculo_o_equipo",
                table: "service_requests",
                sql: "(vehicle_id IS NULL) <> (equipment_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_equipment_id",
                table: "appointments",
                column: "equipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_equipment_customer_id",
                table: "equipment",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_equipment_equipment_type_id",
                table: "equipment",
                column: "equipment_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_equipment_tenant_id_serial_number",
                table: "equipment",
                columns: new[] { "tenant_id", "serial_number" });

            migrationBuilder.CreateIndex(
                name: "ix_equipment_tenant_id_tag",
                table: "equipment",
                columns: new[] { "tenant_id", "tag" });

            migrationBuilder.CreateIndex(
                name: "ix_equipment_types_tenant_id_name",
                table: "equipment_types",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_equipment_equipment_id",
                table: "appointments",
                column: "equipment_id",
                principalTable: "equipment",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_service_requests_equipment_equipment_id",
                table: "service_requests",
                column: "equipment_id",
                principalTable: "equipment",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_work_orders_equipment_equipment_id",
                table: "work_orders",
                column: "equipment_id",
                principalTable: "equipment",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_appointments_equipment_equipment_id",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_service_requests_equipment_equipment_id",
                table: "service_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_work_orders_equipment_equipment_id",
                table: "work_orders");

            migrationBuilder.DropTable(
                name: "equipment");

            migrationBuilder.DropTable(
                name: "equipment_types");

            migrationBuilder.DropIndex(
                name: "ix_work_orders_equipment_id",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_vehiculo_o_equipo",
                table: "work_orders");

            migrationBuilder.DropIndex(
                name: "ix_service_requests_equipment_id",
                table: "service_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_requests_vehiculo_o_equipo",
                table: "service_requests");

            migrationBuilder.DropIndex(
                name: "ix_appointments_equipment_id",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "equipment_id",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "works_with",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "equipment_id",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "equipment_id",
                table: "appointments");

            migrationBuilder.AlterColumn<Guid>(
                name: "vehicle_id",
                table: "work_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "vehicle_id",
                table: "service_requests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
