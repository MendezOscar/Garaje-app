using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FacturacionCai : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customer_tax_id",
                table: "sales",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_cai",
                table: "sales",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fiscal_issue_deadline",
                table: "sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_number",
                table: "sales",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fiscal_range_id",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_range_text",
                table: "sales",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_id",
                table: "customers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fiscal_ranges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cai = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    establishment_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    point_of_sale_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    document_type = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    range_start = table.Column<int>(type: "integer", nullable: false),
                    range_end = table.Column<int>(type: "integer", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false),
                    issue_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_ranges", x => x.id);
                    table.ForeignKey(
                        name: "fk_fiscal_ranges_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_id_fiscal_number",
                table: "sales",
                columns: new[] { "tenant_id", "fiscal_number" },
                unique: true,
                filter: "fiscal_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_ranges_branch_id",
                table: "fiscal_ranges",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_ranges_tenant_id_branch_id_is_active",
                table: "fiscal_ranges",
                columns: new[] { "tenant_id", "branch_id", "is_active" },
                unique: true,
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_ranges");

            migrationBuilder.DropIndex(
                name: "ix_sales_tenant_id_fiscal_number",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "customer_tax_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "fiscal_cai",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "fiscal_issue_deadline",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "fiscal_number",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "fiscal_range_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "fiscal_range_text",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "tax_id",
                table: "customers");
        }
    }
}
