using System.Text.Json;
using System.Text.Json.Serialization;

namespace Garaj.Api.Services;

/// <summary>
/// Deja en UTC cualquier fecha que entre por JSON.
///
/// PostgreSQL guarda estas columnas como <c>timestamptz</c> y Npgsql se niega a escribir un
/// <see cref="DateTimeOffset"/> cuyo desplazamiento no sea cero: revienta con una excepción que
/// el cliente ve como un 500 sin explicación. Un `2026-09-05T17:00:00-06:00` es una fecha
/// perfectamente válida —es la hora de Honduras, la que tiene delante quien la escribe— y no
/// tiene por qué fallar.
///
/// El instante es el mismo; solo cambia cómo se escribe. Al salir, todas las fechas van en UTC,
/// que es lo que los clientes ya esperaban.
/// </summary>
public class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(
        Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime());
}
