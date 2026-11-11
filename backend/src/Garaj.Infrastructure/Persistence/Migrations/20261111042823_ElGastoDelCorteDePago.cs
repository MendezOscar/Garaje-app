using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ElGastoDelCorteDePago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "expense_id",
                table: "assembly_settlements",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expense_id",
                table: "assembly_settlements");
        }
    }
}
