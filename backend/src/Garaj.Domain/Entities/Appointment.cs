using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Cita: el cliente aparta un día para traer el vehículo, antes de que exista orden alguna.
/// </summary>
/// <remarks>
/// Nace de dos lados. Desde la app, el cliente ya es del padrón y elige cuál de sus vehículos
/// trae, así que <see cref="CustomerId"/> y <see cref="VehicleId"/> vienen llenos. Desde el
/// enlace público —el que el taller manda por WhatsApp a quien no va a instalar nada— no hay
/// sesión ni padrón: lo único que se sabe es lo que la persona escribió, y eso se guarda tal
/// cual en <see cref="ContactName"/>, <see cref="ContactPhone"/> y <see cref="VehicleNote"/>.
/// Los datos de verdad se toman al recibir el vehículo, con el carro enfrente.
///
/// Por eso la cita no crea cliente ni vehículo: una agenda llena de gente que nunca llegó no
/// debe ensuciar el padrón.
/// </remarks>
public class Appointment : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }

    /// <summary>Cliente del padrón, cuando la cita nació dentro de la aplicación.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Vehículo del padrón, cuando la cita nació dentro de la aplicación.</summary>
    public Guid? VehicleId { get; set; }

    /// <summary>Cómo se identificó quien pidió la cita.</summary>
    public string ContactName { get; set; } = null!;

    /// <summary>Su teléfono, en el formato de WhatsApp. Es el hilo para devolverle el mensaje.</summary>
    public string ContactPhone { get; set; } = null!;

    /// <summary>Qué vehículo dice que trae, en texto libre. Solo cuando no viene del padrón.</summary>
    public string? VehicleNote { get; set; }

    /// <summary>Qué necesita, con sus palabras.</summary>
    public string Reason { get; set; } = null!;

    /// <summary>El día apartado. Es una fecha, no un instante: una cita no tiene zona horaria.</summary>
    public DateOnly Day { get; set; }

    public AppointmentSlot Slot { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Requested;
    public AppointmentSource Source { get; set; }

    /// <summary>Lo que el taller contestó: el motivo del rechazo o la nota al confirmar.</summary>
    public string? ResponseNote { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>Orden de trabajo que se abrió cuando el vehículo llegó. Null hasta entonces.</summary>
    public Guid? WorkOrderId { get; set; }

    /// <summary>
    /// Token de la página del cliente. Igual que en la cotización, el token **es** la
    /// credencial: con él consulta cómo va su cita y la cancela, sin cuenta ni contraseña.
    /// </summary>
    public Guid PublicToken { get; set; } = Guid.NewGuid();

    public Branch Branch { get; set; } = null!;
    public Customer? Customer { get; set; }
    public Vehicle? Vehicle { get; set; }
}
