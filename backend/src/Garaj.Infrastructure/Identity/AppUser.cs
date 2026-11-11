using Garaj.Domain.Common;
using Garaj.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace Garaj.Infrastructure.Identity;

/// <summary>
/// Usuario de login. Vive en Infrastructure porque depende de ASP.NET Identity; el dominio
/// se refiere a los usuarios solo por Guid para no arrastrar esa dependencia.
/// </summary>
/// <remarks>
/// A diferencia del resto de entidades, <b>no</b> lleva global query filter por tenant: el
/// login tiene que poder encontrar al usuario por email antes de saber a qué taller pertenece.
/// Para listar usuarios use <c>GarajDbContext.UsersInTenant</c>, que sí filtra.
/// </remarks>
public class AppUser : IdentityUser<Guid>, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string FullName { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    /// <summary>Cliente asociado. Solo tiene valor en usuarios con rol Customer.</summary>
    public Guid? CustomerId { get; set; }

    // ---------- Cómo se le paga (solo técnicos) ----------
    //
    // Vive en el usuario y no en una entidad aparte porque es un dato de la persona, no un
    // histórico: lo que se le pagó cada quincena queda en los gastos, que es donde entra a la
    // caja y al estado de resultados.

    public TechnicianPayMode PayMode { get; set; } = TechnicianPayMode.Undefined;

    /// <summary>
    /// El monto de su modalidad: el sueldo fijo, el porcentaje, o la tarifa por hora. Cero
    /// mientras no se defina.
    /// </summary>
    public decimal PayAmount { get; set; }

    /// <summary>
    /// Lo que se le paga por cada moto que arma, sea el modelo que sea. Null es lo normal:
    /// entonces se le paga lo que diga la tarifa de ese modelo con ese contratista.
    /// </summary>
    /// <remarks>
    /// Existe porque el pago al técnico no siempre sigue al modelo: hay quien está contratado
    /// a tanto por moto armada, arme la que arme, y hay quien cobra lo que valga cada una.
    /// Aparte de <see cref="PayAmount"/> a propósito: aquello es cómo se le paga el trabajo
    /// del taller —sueldo, porcentaje u hora—, y esto es el armado, que se cobra por pieza y
    /// se liquida en su propio corte.
    ///
    /// Como todo lo del armado, se <b>copia</b> a la moto al registrarla: subirle el trato en
    /// marzo no reescribe lo que ya se le pagó en enero.
    /// </remarks>
    public decimal? AssemblyFixedPay { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserBranch> Branches { get; set; } = new List<UserBranch>();
}

public class AppRole : IdentityRole<Guid>
{
    public AppRole() { }

    public AppRole(string name) : base(name) { }
}
