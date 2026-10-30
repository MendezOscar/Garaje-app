using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnularElPagoDeSuscripcion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_voided",
                table: "subscription_payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "previous_paid_through",
                table: "subscription_payments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                table: "subscription_payments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                table: "subscription_payments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_voided",
                table: "subscription_payments");

            migrationBuilder.DropColumn(
                name: "previous_paid_through",
                table: "subscription_payments");

            migrationBuilder.DropColumn(
                name: "void_reason",
                table: "subscription_payments");

            migrationBuilder.DropColumn(
                name: "voided_at",
                table: "subscription_payments");
        }
    }
}
