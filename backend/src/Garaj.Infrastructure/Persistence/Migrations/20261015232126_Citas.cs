using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Citas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "booking_enabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "booking_token",
                table: "tenants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Cada taller estrena su propio enlace. Sin esto, los que ya existen se quedarían
            // todos con el uuid cero y el índice único de abajo no se podría crear.
            migrationBuilder.Sql("UPDATE tenants SET booking_token = gen_random_uuid();");

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vehicle_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    response_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    public_token = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointments_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_booking_token",
                table: "tenants",
                column: "booking_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointments_branch_id",
                table: "appointments",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_customer_id",
                table: "appointments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_public_token",
                table: "appointments",
                column: "public_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointments_tenant_id_branch_id_day_status",
                table: "appointments",
                columns: new[] { "tenant_id", "branch_id", "day", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_tenant_id_contact_phone",
                table: "appointments",
                columns: new[] { "tenant_id", "contact_phone" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_vehicle_id",
                table: "appointments",
                column: "vehicle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_tenants_booking_token",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "booking_enabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "booking_token",
                table: "tenants");
        }
    }
}
