namespace Garaj.Application.Assembly;

/// <summary>Un contratista con su tabla de modelos: lo que paga y lo que se le paga al técnico.</summary>
public record AssemblyContractorDto(
    Guid Id,
    string Name,
    string? Notes,
    bool IsActive,
    IReadOnlyList<AssemblyRateDto> Rates);

/// <param name="Charge">Lo que el contratista paga por armar una. No lo ve el técnico.</param>
/// <param name="TechnicianPay">Lo que el taller le paga al técnico que la arma.</param>
public record AssemblyRateDto(
    Guid ModelId,
    string ModelName,
    decimal Charge,
    decimal TechnicianPay);

public record SaveAssemblyContractorRequest(string Name, string? Notes, bool IsActive = true);

/// <summary>
/// La tabla entera de un contratista, de una vez.
/// </summary>
/// <remarks>
/// Se manda completa y reemplaza lo que había: es una tabla que se llena de un tirón al abrir
/// el contrato y se repasa cuando se renegocia. Guardar fila por fila obligaría a llevar el
/// estado de cuáles se tocaron, para resolver un problema que no existe.
/// </remarks>
public record SaveAssemblyRatesRequest(IReadOnlyList<SaveAssemblyRateLine> Rates);

public record SaveAssemblyRateLine(Guid ModelId, decimal Charge, decimal TechnicianPay);

public record AssemblyModelDto(Guid Id, string Name, bool IsActive);

public record SaveAssemblyModelRequest(string Name, bool IsActive = true);

public record AssemblyWarehouseDto(Guid Id, string Name, bool IsActive);

public record SaveAssemblyWarehouseRequest(string Name, bool IsActive = true);

/// <summary>
/// El catálogo del contrato: con quién se trabaja, qué modelos se arman y en qué bodegas.
/// </summary>
/// <remarks>
/// Es del Dueño entero. De aquí salen los dos precios, y uno de ellos —lo que se le cobra al
/// contratista— es justo lo que no puede ver el técnico.
/// </remarks>
public interface IAssemblyCatalogService
{
    Task<IReadOnlyList<AssemblyContractorDto>> ContractorsAsync(CancellationToken ct = default);

    Task<AssemblyContractorDto> SaveContractorAsync(
        Guid? id, SaveAssemblyContractorRequest request, CancellationToken ct = default);

    Task<AssemblyContractorDto> SaveRatesAsync(
        Guid contractorId, SaveAssemblyRatesRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AssemblyModelDto>> ModelsAsync(CancellationToken ct = default);

    Task<AssemblyModelDto> SaveModelAsync(
        Guid? id, SaveAssemblyModelRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AssemblyWarehouseDto>> WarehousesAsync(CancellationToken ct = default);

    Task<AssemblyWarehouseDto> SaveWarehouseAsync(
        Guid? id, SaveAssemblyWarehouseRequest request, CancellationToken ct = default);
}

/// <summary>Una moto armada, como la ve quien la registró o quien la factura.</summary>
/// <param name="Charge">
/// Lo que se le cobra al contratista. <b>Null para el técnico</b>: no se le esconde en la
/// pantalla, no sale del servidor.
/// </param>
public record MotorcycleAssemblyDto(
    Guid Id,
    DateOnly AssembledOn,
    string Serial,
    Guid ModelId,
    string ModelName,
    Guid ContractorId,
    string ContractorName,
    Guid WarehouseId,
    string WarehouseName,
    Guid TechnicianUserId,
    string TechnicianName,
    decimal TechnicianPay,
    string? Notes,
    decimal? Charge);

public record RegisterMotorcycleRequest(
    string Serial,
    Guid ModelId,
    Guid ContractorId,
    Guid WarehouseId,
    DateOnly? AssembledOn = null,
    Guid? BranchId = null,
    string? Notes = null);

/// <summary>
/// Lo que el técnico necesita a mano para registrar sin teclear: con quién se trabaja, qué
/// modelos y en qué bodegas. Sin un solo precio.
/// </summary>
public record AssemblyPickListsDto(
    IReadOnlyList<AssemblyNameDto> Contractors,
    IReadOnlyList<AssemblyNameDto> Models,
    IReadOnlyList<AssemblyNameDto> Warehouses);

public record AssemblyNameDto(Guid Id, string Name);

/// <summary>
/// Lo que lleva armado quien pregunta en un periodo, con lo que suma.
/// </summary>
/// <param name="Pay">Lo que se le debe por esas motos. Es su número, no el del taller.</param>
public record MyAssembliesDto(
    DateOnly From,
    DateOnly To,
    int Count,
    decimal Pay,
    IReadOnlyList<MotorcycleAssemblyDto> Items);

/// <summary>
/// El modelo que probablemente le corresponde a una serie, aprendido de lo ya registrado.
/// </summary>
/// <remarks>
/// No hay tabla que comprar: los primeros ocho caracteres del VIN son el fabricante y la
/// descripción del vehículo, y lo que significan es la tabla interna de cada marca. Así que
/// se aprende de este taller: la primera moto de cada prefijo se teclea, y de la segunda en
/// adelante se propone. Es siempre una propuesta; la confirma quien tiene la moto delante.
/// </remarks>
public record ModelGuessDto(Guid? ModelId, string? ModelName, int SeenBefore);

public interface IMotorcycleAssemblyService
{
    Task<AssemblyPickListsDto> PickListsAsync(CancellationToken ct = default);

    Task<MotorcycleAssemblyDto> RegisterAsync(
        RegisterMotorcycleRequest request, CancellationToken ct = default);

    Task<MyAssembliesDto> MineAsync(
        DateOnly? from, DateOnly? to, CancellationToken ct = default);

    Task<ModelGuessDto> GuessModelAsync(string serial, CancellationToken ct = default);
}
