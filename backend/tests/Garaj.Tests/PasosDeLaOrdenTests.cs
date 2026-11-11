using Garaj.Domain.Enums;
using Garaj.Domain.Rules;

namespace Garaj.Tests;

/// <summary>
/// El flujo de la orden cuando el taller apaga pasos. Se prueba aquí y no por HTTP porque
/// esta tabla decide dos cosas caras de equivocar: dejar una orden sin salida —el taller no
/// puede entregar y nadie entiende por qué— y ofrecer un paso que el taller apagó.
/// </summary>
public class PasosDeLaOrdenTests
{
    /// <summary>El taller que pidió el flujo corto: abrir, cargar, facturar.</summary>
    private const WorkOrderSteps Corto = WorkOrderSteps.None;

    [Fact]
    public void ConTodosLosPasosElFlujoNoCambia()
    {
        // La tabla de siempre, tal cual: los talleres que ya trabajan no pueden notar nada.
        Assert.Equal(
            WorkOrderStatusTransitions.NextStatuses(WorkOrderStatus.Received),
            WorkOrderStatusTransitions.NextStatuses(WorkOrderStatus.Received, WorkOrderSteps.All));

        Assert.Equal(
            WorkOrderStatusTransitions.NextStatuses(WorkOrderStatus.InProgress),
            WorkOrderStatusTransitions.NextStatuses(WorkOrderStatus.InProgress, WorkOrderSteps.All));
    }

    [Fact]
    public void SinNingunPasoElCaminoEsRecibidaEnProcesoEntregada()
    {
        Assert.Equal(
            [WorkOrderStatus.InProgress, WorkOrderStatus.Cancelled],
            WorkOrderStatusTransitions.NextStatuses(WorkOrderStatus.Received, Corto));

        Assert.Equal(
            [WorkOrderStatus.Delivered, WorkOrderStatus.Cancelled],
            WorkOrderStatusTransitions.NextStatuses(WorkOrderStatus.InProgress, Corto));

        Assert.Empty(WorkOrderStatusTransitions.NextStatuses(WorkOrderStatus.Delivered, Corto));
    }

    [Fact]
    public void UnPasoApagadoNoSeOfreceNiSeAcepta()
    {
        Assert.DoesNotContain(
            WorkOrderStatus.Diagnosing,
            WorkOrderStatusTransitions.NextStatuses(WorkOrderStatus.Received, Corto));

        Assert.False(WorkOrderStatusTransitions.CanTransition(
            WorkOrderStatus.Received, WorkOrderStatus.Diagnosing, Corto));

        // Y el salto que el grafo canónico no tiene sí se acepta.
        Assert.True(WorkOrderStatusTransitions.CanTransition(
            WorkOrderStatus.Received, WorkOrderStatus.InProgress, Corto));
        Assert.False(WorkOrderStatusTransitions.CanTransition(
            WorkOrderStatus.Received, WorkOrderStatus.InProgress));
    }

    [Fact]
    public void LaOrdenParadaEnUnPasoQueSeApagoSigueTeniendoSalida()
    {
        // El taller apagó «Esperando repuestos» con una orden parada justo ahí. Si esa orden
        // se quedara sin salida habría que tocarle la base de datos para entregarla.
        var siguientes = WorkOrderStatusTransitions.NextStatuses(
            WorkOrderStatus.WaitingParts, WorkOrderSteps.All & ~WorkOrderSteps.WaitingParts);

        Assert.Contains(WorkOrderStatus.InProgress, siguientes);
    }

    [Fact]
    public void ApagarElDiagnosticoNoSeLlevaLoQueEstabaDetrasDeEl()
    {
        // Detrás del diagnóstico están las dos esperas. El taller que lo apaga pero sigue
        // esperando repuestos tiene que poder parar la orden desde que la recibe.
        var siguientes = WorkOrderStatusTransitions.NextStatuses(
            WorkOrderStatus.Received, WorkOrderSteps.All & ~WorkOrderSteps.Diagnosing);

        Assert.Equal(
            [
                WorkOrderStatus.WaitingApproval,
                WorkOrderStatus.WaitingParts,
                WorkOrderStatus.InProgress,
                WorkOrderStatus.Cancelled
            ],
            siguientes);
    }

    [Fact]
    public void ElEstadoDeDondeSeParteNuncaSeOfrece()
    {
        // «En pruebas» apagado deja a «En proceso» rodeando hacia sí mismo por el grafo.
        var siguientes = WorkOrderStatusTransitions.NextStatuses(
            WorkOrderStatus.InProgress, WorkOrderSteps.All & ~WorkOrderSteps.Testing);

        Assert.DoesNotContain(WorkOrderStatus.InProgress, siguientes);
        Assert.Contains(WorkOrderStatus.Ready, siguientes);
    }

    [Fact]
    public void CancelarYEntregarNuncaSeApagan()
    {
        foreach (var desde in new[]
                 {
                     WorkOrderStatus.Received, WorkOrderStatus.InProgress
                 })
        {
            Assert.Contains(
                WorkOrderStatus.Cancelled,
                WorkOrderStatusTransitions.NextStatuses(desde, Corto));
        }

        Assert.Contains(
            WorkOrderStatus.Delivered,
            WorkOrderStatusTransitions.NextStatuses(WorkOrderStatus.InProgress, Corto));
    }
}
