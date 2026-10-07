using Garaj.Domain.Rules;

namespace Garaj.Tests;

/// <summary>
/// El portón de versión. Se prueba aquí porque las dos equivocaciones son caras y opuestas:
/// dejar pasar una app que ya no entiende la API, y —mucho peor— tumbarle el sistema a un
/// taller que está al día o que ni siquiera usa la app.
/// </summary>
public class VersionDeLaAppTests
{
    [Theory]
    [InlineData("GarajApp/1.3.0+12", 12)]
    [InlineData("GarajApp/1.2.0+9", 9)]
    [InlineData("  GarajApp/2.0.0+100  ", 100)]
    public void Lee_la_compilacion_de_la_cabecera(string cabecera, int esperado) =>
        Assert.Equal(esperado, VersionDeLaApp.LeerCompilacion(cabecera));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GarajApp/desconocida")]
    [InlineData("GarajApp/1.3.0")]
    [InlineData("GarajApp/1.3.0+")]
    [InlineData("GarajApp/1.3.0+abc")]
    [InlineData("cualquier cosa")]
    public void Lo_que_no_se_entiende_no_trae_compilacion(string? cabecera) =>
        Assert.Null(VersionDeLaApp.LeerCompilacion(cabecera));

    [Fact]
    public void Sin_version_pasa_siempre()
    {
        // Es el panel web, que no manda la cabecera. Bloquearlo sería tumbarlo por algo que
        // nunca prometió mandar.
        Assert.Equal(ExigenciaDeVersion.Pasa, VersionDeLaApp.Decidir(null, 99, 99));
        Assert.Equal(ExigenciaDeVersion.Pasa, VersionDeLaApp.DecidirPorCabecera(null, 99, 99));
    }

    [Fact]
    public void En_cero_no_estorba_a_nadie()
    {
        // Como viene de fábrica: sin números puestos, nadie se queda sin app.
        Assert.Equal(ExigenciaDeVersion.Pasa, VersionDeLaApp.Decidir(1, 0, 0));
    }

    [Fact]
    public void Debajo_del_minimo_se_bloquea()
    {
        Assert.Equal(ExigenciaDeVersion.Bloquear, VersionDeLaApp.Decidir(11, 12, 12));
    }

    [Fact]
    public void Justo_en_el_minimo_pasa()
    {
        // El borde: la versión que se fija como mínima es la que se deja trabajar.
        Assert.Equal(ExigenciaDeVersion.Pasa, VersionDeLaApp.Decidir(12, 12, 12));
    }

    [Fact]
    public void Entre_el_minimo_y_la_recomendada_solo_se_avisa()
    {
        Assert.Equal(ExigenciaDeVersion.Avisar, VersionDeLaApp.Decidir(12, 10, 15));
    }

    [Fact]
    public void Mas_nueva_que_la_recomendada_pasa()
    {
        // La del desarrollador, que siempre va por delante de lo que hay publicado.
        Assert.Equal(ExigenciaDeVersion.Pasa, VersionDeLaApp.Decidir(20, 10, 15));
    }

    [Fact]
    public void El_bloqueo_manda_sobre_el_aviso()
    {
        Assert.Equal(ExigenciaDeVersion.Bloquear, VersionDeLaApp.Decidir(5, 10, 20));
    }
}
