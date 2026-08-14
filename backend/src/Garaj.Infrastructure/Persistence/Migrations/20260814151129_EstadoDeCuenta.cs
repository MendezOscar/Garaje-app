using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstadoDeCuenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "public_token",
                table: "customers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Un token por cliente antes de crear el índice único: si no, los que ya están en
            // la base quedarían todos con el GUID vacío y el índice fallaría al crearse. Los
            // nuevos lo traen del constructor de la entidad.
            migrationBuilder.Sql("UPDATE customers SET public_token = gen_random_uuid();");

            migrationBuilder.CreateIndex(
                name: "ix_customers_public_token",
                table: "customers",
                column: "public_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_public_token",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "public_token",
                table: "customers");
        }
    }
}
