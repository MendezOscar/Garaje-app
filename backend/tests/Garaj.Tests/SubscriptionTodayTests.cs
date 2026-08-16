using Garaj.Application.Abstractions;

namespace Garaj.Tests;

/// <summary>
/// El «hoy» del cobro. Parece un detalle y no lo es: el servidor corre en UTC y el día UTC
/// cambia a las 6 de la tarde de Honduras, así que contándolo mal el taller pagado hasta el
/// jueves se quedaba sin sistema el jueves a las seis, justo mientras cierra el día.
/// </summary>
public class SubscriptionTodayTests
{
    private sealed class RelojFijo(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    [Theory]
    // Mediodía en Honduras: las 18:00 UTC del mismo día.
    [InlineData("2026-08-20T18:00:00Z", "2026-08-20")]
    // Las 5:59 de la tarde de acá. En UTC ya son las 23:59, todavía el mismo día.
    [InlineData("2026-08-20T23:59:00Z", "2026-08-20")]
    // Las 6:01 de la tarde de acá: el día UTC ya cambió, el de Honduras no. Éste es el caso.
    [InlineData("2026-08-21T00:01:00Z", "2026-08-20")]
    // Medianoche en Honduras, las 6 de la mañana UTC: ahora sí es otro día.
    [InlineData("2026-08-21T06:00:00Z", "2026-08-21")]
    public void Hoy_es_el_dia_de_Honduras_no_el_del_servidor(string utc, string esperado)
    {
        var reloj = new RelojFijo(DateTimeOffset.Parse(utc));

        Assert.Equal(DateOnly.Parse(esperado), reloj.Today());
    }
}
