using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaSucursalDelContrato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                table: "assembly_contractors",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_assembly_contractors_branch_id",
                table: "assembly_contractors",
                column: "branch_id");

            migrationBuilder.AddForeignKey(
                name: "fk_assembly_contractors_branches_branch_id",
                table: "assembly_contractors",
                column: "branch_id",
                principalTable: "branches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // A los contratos que ya existen se les pone la sucursal donde ya están sus motos
            // armadas, y si no tienen ninguna, la primera del taller. Dejarlos en nulo haría
            // que la primera moto de cada uno cayera en la sucursal de quien la registrara.
            migrationBuilder.Sql("""
                UPDATE assembly_contractors c
                SET branch_id = COALESCE(
                    (SELECT m.branch_id
                     FROM motorcycle_assemblies m
                     WHERE m.contractor_id = c.id
                     GROUP BY m.branch_id
                     ORDER BY COUNT(*) DESC
                     LIMIT 1),
                    (SELECT b.id
                     FROM branches b
                     WHERE b.tenant_id = c.tenant_id AND b.is_active
                     ORDER BY b.name
                     LIMIT 1));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assembly_contractors_branches_branch_id",
                table: "assembly_contractors");

            migrationBuilder.DropIndex(
                name: "ix_assembly_contractors_branch_id",
                table: "assembly_contractors");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "assembly_contractors");
        }
    }
}
