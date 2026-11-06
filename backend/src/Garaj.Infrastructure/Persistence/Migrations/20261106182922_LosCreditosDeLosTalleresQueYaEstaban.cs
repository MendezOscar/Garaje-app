using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garaj.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Les pone los créditos del plan a los talleres que ya estaban.
    /// </summary>
    /// <remarks>
    /// La migración anterior dejó las columnas nuevas en cero para todas las filas que ya
    /// existían, que es lo que hace EF. Para <c>assistant_enabled</c> está bien —nace apagado
    /// a propósito, se enciende taller por taller—, pero para los créditos no: un taller Pro
    /// al que se le encienda el asistente con los créditos en cero se encontraría con que no
    /// funciona nada y con un mensaje que no explica por qué.
    ///
    /// Solo toca las filas que siguen en cero, así que correrla dos veces no cambia nada, y no
    /// pisa a ningún taller al que ya se le hayan ajustado los números a mano.
    ///
    /// Los planes son los del enum: 0 heredado, 1 básico, 2 Pro. El asistente es del Pro, y los
    /// heredados lo llevan por lo mismo que llevan todo lo demás: se les ofreció sin topes.
    /// </remarks>
    public partial class LosCreditosDeLosTalleresQueYaEstaban : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE tenants
                SET assistant_credits_per_month = 450,
                    assistant_credits_per_day = 80,
                    assistant_monthly_cap_hnl = 220
                WHERE plan IN (0, 2)
                  AND assistant_credits_per_month = 0
                  AND assistant_credits_per_day = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Devolverlos a cero borraría números que después se pudieron negociar taller por
            // taller. Se deja como está: la columna sigue existiendo y el asistente, apagado.
        }
    }
}
