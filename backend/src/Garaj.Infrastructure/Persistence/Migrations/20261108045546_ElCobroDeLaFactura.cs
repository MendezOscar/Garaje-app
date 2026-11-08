using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ElCobroDeLaFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "collected_on",
                table: "assembly_settlements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "collected_reference",
                table: "assembly_settlements",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "collected_on",
                table: "assembly_settlements");

            migrationBuilder.DropColumn(
                name: "collected_reference",
                table: "assembly_settlements");
        }
    }
}
