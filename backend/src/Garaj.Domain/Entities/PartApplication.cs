using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// A qué vehículo le sirve un repuesto: marca, modelo y años. Un repuesto tiene varias.
/// </summary>
/// <remarks>
/// Nace de lo que pasa en el mostrador: quien atiende tiene que saberse de memoria qué filtro
/// le va al Corolla 2016 y cuál al Yaris, y cuando no está el que sabe se vende el equivocado.
/// <para>
/// Es una tabla aparte y no una columna de texto en <see cref="Part"/> porque el filtro de
/// «solo lo que le sirve a este carro» tiene que preguntar por años, y un rango escrito dentro
/// de una frase no se puede comparar.
/// </para>
/// <para>
/// Todo es opcional menos la marca: el dato se llena a mano y va a estar incompleto durante
/// mucho tiempo. Un filtro que sirve para todos los Corolla se guarda sin años, y uno que no
/// distingue motor se guarda sin motor, en vez de obligar a inventarlos.
/// </para>
/// </remarks>
public class PartApplication : TenantEntity
{
    public Guid PartId { get; set; }

    public string Brand { get; set; } = null!;

    /// <summary>Sin modelo, aplica a toda la marca.</summary>
    public string? Model { get; set; }

    /// <summary>Desde qué año. Sin años, aplica a todos.</summary>
    public int? YearFrom { get; set; }

    public int? YearTo { get; set; }

    /// <summary>Cuando el mismo modelo lleva repuestos distintos según el motor: «1.8», «diésel».</summary>
    public string? Engine { get; set; }

    public Part Part { get; set; } = null!;
}
