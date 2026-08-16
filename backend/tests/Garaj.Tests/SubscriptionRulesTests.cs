using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Domain.Rules;

namespace Garaj.Tests;

/// <summary>
/// La tabla de verdad del cobro. Se prueba aquí y no por HTTP porque esta función decide dos
/// cosas caras de equivocar: cortarle el sistema a un taller que sí pagó, y dejarlo trabajar
/// gratis. Los casos son los bordes, que es donde se cae: el día exacto del vencimiento, el
/// último día de gracia y el primero sin ella.
/// </summary>
public class SubscriptionRulesTests
{
    private static readonly DateOnly Hoy = new(2026, 8, 15);

    private static Tenant Taller(
        DateOnly? paidThrough,
        int graceDays = 5,
        bool isActive = true,
        DateOnly? unblockedThrough = null) => new()
    {
        Name = "Taller",
        IsActive = isActive,
        PaidThrough = paidThrough,
        GraceDays = graceDays,
        UnblockedThrough = unblockedThrough
    };

    [Theory]
    // Falta más de una semana: ni se le menciona.
    [InlineData(30, SubscriptionState.Active)]
    [InlineData(8, SubscriptionState.Active)]
    // Entra la ventana de aviso, pero trabaja igual.
    [InlineData(7, SubscriptionState.DueSoon)]
    [InlineData(1, SubscriptionState.DueSoon)]
    // El mismo día del vencimiento todavía está pagado.
    [InlineData(0, SubscriptionState.DueSoon)]
    // Vencido, dentro de los cinco días de tolerancia.
    [InlineData(-1, SubscriptionState.Grace)]
    [InlineData(-5, SubscriptionState.Grace)]
    // Al sexto día se acabó.
    [InlineData(-6, SubscriptionState.ReadOnly)]
    [InlineData(-60, SubscriptionState.ReadOnly)]
    public void Resuelve_el_estado_por_los_dias_que_faltan(int diasParaVencer, SubscriptionState esperado)
    {
        var estado = SubscriptionRules.For(Taller(Hoy.AddDays(diasParaVencer)), Hoy);

        Assert.Equal(esperado, estado.State);
        Assert.Equal(diasParaVencer, estado.DaysLeft);
    }

    [Theory]
    [InlineData(SubscriptionState.Active, true)]
    [InlineData(SubscriptionState.DueSoon, true)]
    [InlineData(SubscriptionState.Grace, true)]
    [InlineData(SubscriptionState.ReadOnly, false)]
    [InlineData(SubscriptionState.Suspended, false)]
    public void Solo_el_vencido_y_el_suspendido_dejan_de_escribir(SubscriptionState estado, bool puedeEscribir)
    {
        var status = new SubscriptionStatus(estado, null, null, null, null, null);

        Assert.Equal(puedeEscribir, status.CanWrite);
    }

    [Fact]
    public void Sin_fecha_de_pago_no_se_bloquea_nunca()
    {
        // Los talleres que existían antes de que esto se cobrara. El silencio no puede
        // convertirse en un corte.
        var estado = SubscriptionRules.For(Taller(paidThrough: null), Hoy);

        Assert.Equal(SubscriptionState.Active, estado.State);
        Assert.True(estado.CanWrite);
        Assert.False(estado.ShouldWarn);
    }

    [Fact]
    public void El_acuerdo_de_pago_devuelve_el_trabajo_a_un_taller_vencido()
    {
        var vencido = Taller(Hoy.AddDays(-40), unblockedThrough: Hoy.AddDays(10));

        var estado = SubscriptionRules.For(vencido, Hoy);

        Assert.Equal(SubscriptionState.Active, estado.State);
        Assert.True(estado.CanWrite);
        // Sigue avisando: es la razón por la que puede trabajar, y se le termina.
        Assert.True(estado.ShouldWarn);
        Assert.Equal(Hoy.AddDays(10), estado.AgreementThrough);
    }

    [Fact]
    public void Un_acuerdo_vencido_ayer_ya_no_vale()
    {
        var vencido = Taller(Hoy.AddDays(-40), unblockedThrough: Hoy.AddDays(-1));

        var estado = SubscriptionRules.For(vencido, Hoy);

        Assert.Equal(SubscriptionState.ReadOnly, estado.State);
        Assert.Null(estado.AgreementThrough);
    }

    [Fact]
    public void El_acuerdo_vale_hasta_el_ultimo_dia_inclusive()
    {
        var estado = SubscriptionRules.For(Taller(Hoy.AddDays(-40), unblockedThrough: Hoy), Hoy);

        Assert.True(estado.CanWrite);
    }

    [Fact]
    public void La_suspension_gana_sobre_el_acuerdo_de_pago()
    {
        // Suspender es una decisión explícita nuestra; no la revierte una fecha que quedó puesta.
        var suspendido = Taller(Hoy.AddDays(30), isActive: false, unblockedThrough: Hoy.AddDays(10));

        var estado = SubscriptionRules.For(suspendido, Hoy);

        Assert.Equal(SubscriptionState.Suspended, estado.State);
        Assert.False(estado.CanWrite);
    }

    [Fact]
    public void Sin_dias_de_gracia_se_bloquea_al_dia_siguiente()
    {
        var sinGracia = Taller(Hoy.AddDays(-1), graceDays: 0);

        Assert.Equal(SubscriptionState.ReadOnly, SubscriptionRules.For(sinGracia, Hoy).State);
    }

    [Fact]
    public void Avisa_el_dia_en_que_dejara_de_poder_trabajar()
    {
        var estado = SubscriptionRules.For(Taller(Hoy.AddDays(2), graceDays: 5), Hoy);

        // Vence el 17, cinco días de tolerancia: el 22 todavía trabaja, el 23 ya no.
        Assert.Equal(new DateOnly(2026, 8, 23), estado.ReadOnlyOn);
    }
}
