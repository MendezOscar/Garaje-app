using System.Reflection;
using Garaj.Application.Appointments;
using Garaj.Domain.Rules;

namespace Garaj.Tests;

/// <summary>
/// Las citas.
///
/// Lo delicado no es agendar: es el enlace público, que recibe lo que escriba cualquiera sin
/// sesión de por medio. Por eso se prueban las dos reglas que lo sostienen —qué día se acepta
/// y qué teléfono sirve— y que lo que esa página devuelve no lleve nada con lo que llegar al
/// resto del taller.
/// </summary>
public class CitasTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 15);

    [Fact]
    public void Hoy_se_puede_agendar()
    {
        // El que llama a las nueve de la mañana para traerlo a las dos.
        Assert.True(AppointmentRules.EsAgendable(Hoy, Hoy));
    }

    [Fact]
    public void Un_dia_que_ya_paso_no()
    {
        Assert.False(AppointmentRules.EsAgendable(Hoy.AddDays(-1), Hoy));
    }

    [Fact]
    public void El_limite_de_adelante_entra_y_el_siguiente_no()
    {
        var ultimo = Hoy.AddDays(AppointmentRules.DiasQueSeAgendan);

        Assert.True(AppointmentRules.EsAgendable(ultimo, Hoy));
        Assert.False(AppointmentRules.EsAgendable(ultimo.AddDays(1), Hoy));
    }

    [Theory]
    [InlineData("9988-7766", "99887766")]
    [InlineData("+504 9988 7766", "50499887766")]
    [InlineData(" (504) 9988.7766 ", "50499887766")]
    public void El_telefono_se_queda_en_digitos(string escrito, string esperado)
    {
        // Lo escribe quien quiere, en el teléfono y con prisa; wa.me solo acepta dígitos.
        Assert.Equal(esperado, AppointmentRules.Telefono(escrito));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567")]
    [InlineData("no tengo")]
    public void Sin_telefono_utilizable_no_hay_cita(string? escrito)
    {
        // Es lo único que de verdad hace falta: sin número no hay cómo confirmarle.
        Assert.Null(AppointmentRules.Telefono(escrito));
    }

    [Fact]
    public void La_pagina_publica_no_devuelve_ningun_id_del_taller()
    {
        // La abre un desconocido. Un id de sucursal o de cliente ahí dentro es el primer
        // peldaño para preguntarle a la API por algo que no es suyo.
        var propiedades = typeof(PublicAppointmentDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("BranchId", propiedades);
        Assert.DoesNotContain("CustomerId", propiedades);
        Assert.DoesNotContain("VehicleId", propiedades);
        Assert.DoesNotContain("TenantId", propiedades);
        Assert.DoesNotContain("ContactPhone", propiedades);

        // Lo que sí: su propio token, para volver a abrirla.
        Assert.Contains("Token", propiedades);
    }

    [Fact]
    public void La_hoja_del_enlace_solo_dice_lo_que_esta_en_la_puerta_del_local()
    {
        var propiedades = typeof(BookingPageDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Equal(
            new[] { "Branches", "Enabled", "HasLogo", "LastDay", "TenantName" },
            propiedades.Order());
    }
}
