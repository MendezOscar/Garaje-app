namespace Garaj.Domain.Common;

/// <summary>
/// Entidad que pertenece a un taller (tenant). El <see cref="GarajDbContext"/> aplica un
/// global query filter sobre esta propiedad: es la barrera que impide que un taller
/// lea datos de otro. Toda entidad de negocio debe implementarla.
/// </summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

/// <summary>Entidad que pertenece a una sucursal concreta del taller.</summary>
public interface IBranchEntity
{
    Guid BranchId { get; set; }
}

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedByUserId { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedByUserId { get; set; }
}

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public abstract class AuditableEntity : BaseEntity, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>Base para entidades de negocio del taller: auditadas y aisladas por tenant.</summary>
public abstract class TenantEntity : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
}
