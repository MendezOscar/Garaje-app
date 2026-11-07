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
