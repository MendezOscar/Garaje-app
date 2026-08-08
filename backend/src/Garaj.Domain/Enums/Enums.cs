namespace Garaj.Domain.Enums;

public enum VehicleType
{
    Car = 1,
    Motorcycle = 2
}

/// <summary>Estado del requerimiento que entra al taller, antes de convertirse en orden de trabajo.</summary>
public enum ServiceRequestStatus
{
    Pending = 1,
    Quoted = 2,
    Approved = 3,
    Rejected = 4,
    Converted = 5
}

/// <summary>
/// Flujo normal: Received → Diagnosing → WaitingApproval → WaitingParts → InProgress → Testing → Ready → Delivered.
/// Las transiciones válidas se validan en <c>WorkOrderStatusTransitions</c>.
/// </summary>
public enum WorkOrderStatus
{
    Received = 1,
    Diagnosing = 2,
    WaitingApproval = 3,
    WaitingParts = 4,
    InProgress = 5,
    Testing = 6,
    Ready = 7,
    Delivered = 8,
    Cancelled = 9
}

public enum MediaOwnerType
{
    ServiceRequest = 1,
    WorkOrder = 2,
    WorkOrderTask = 3
}

public enum StockMovementType
{
    In = 1,
    Out = 2,
    Adjustment = 3,
    TransferIn = 4,
    TransferOut = 5
}

public enum QuoteStatus
{
    Draft = 1,
    Sent = 2,
    Approved = 3,
    Rejected = 4,
    Expired = 5
}

/// <summary>Distingue ingresos por repuestos de ingresos por mano de obra en cotizaciones y ventas.</summary>
public enum LineType
{
    Part = 1,
    Labor = 2
}

public enum PaymentMethod
{
    Cash = 1,
    Card = 2,
    Transfer = 3,
    Other = 4
}

/// <summary>
/// Motivo del aviso. Determina el ícono y a qué pantalla lleva al tocarlo, así que los
/// valores no se reordenan: los clientes ya publicados los guardan como enteros.
/// </summary>
public enum NotificationType
{
    ServiceRequestCreated = 1,
    WorkOrderAssigned = 2,
    WorkOrderStatusChanged = 3,
    QuoteSent = 4,
    QuoteAnswered = 5
}

public enum DevicePlatform
{
    Android = 1,
    IOS = 2,
    Web = 3
}
