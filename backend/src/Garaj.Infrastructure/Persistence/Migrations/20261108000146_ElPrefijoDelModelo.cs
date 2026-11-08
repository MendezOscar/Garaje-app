using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ElPrefijoDelModelo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "vin_prefix",
                table: "assembly_models",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_assembly_models_tenant_id_vin_prefix",
                table: "assembly_models",
                columns: new[] { "tenant_id", "vin_prefix" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assembly_models_tenant_id_vin_prefix",
                table: "assembly_models");

            migrationBuilder.DropColumn(
                name: "vin_prefix",
                table: "assembly_models");
        }
    }
}
