using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Persistence;

/// <summary>
/// Las categorías con las que arranca un taller de equipos. Sin ninguna no se puede dar de
/// alta un solo aparato, así que se siembran al crearlo; son un punto de partida y el taller
/// las renombra, las apaga o agrega las suyas desde Ajustes.
/// </summary>
public static class EquipmentTypeSeeder
{
    private static readonly (string Nombre, string Icono)[] Iniciales =
    [
        ("Computadora", "laptop"),
        ("Teléfono", "smartphone"),
        ("Aire acondicionado", "ac_unit"),
        ("Televisor", "tv"),
        ("Electrodoméstico", "kitchen"),
        ("Otro", "devices_other"),
    ];

    /// <summary>Idempotente: si el taller ya tiene categorías, no toca nada.</summary>
    public static async Task EnsureAsync(GarajDbContext db, CancellationToken ct = default)
    {
        if (await db.EquipmentTypes.AnyAsync(ct)) return;

        foreach (var (nombre, icono) in Iniciales)
            db.EquipmentTypes.Add(new EquipmentType { Name = nombre, Icon = icono });

        await db.SaveChangesAsync(ct);
    }
}
