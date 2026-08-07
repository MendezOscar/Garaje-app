using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class PartConfiguration : IEntityTypeConfiguration<Part>
{
    public void Configure(EntityTypeBuilder<Part> b)
    {
        b.Property(x => x.Sku).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Brand).HasMaxLength(80);
        b.Property(x => x.Category).HasMaxLength(80);
        b.Property(x => x.Unit).HasMaxLength(10).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Name });
    }
}

public class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> b)
    {
        b.Property(x => x.Location).HasMaxLength(80);

        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Part).WithMany(p => p.StockItems).HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Cascade);

        // Una sola fila de existencia por repuesto y sucursal.
        b.HasIndex(x => new { x.BranchId, x.PartId }).IsUnique();
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);

        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);

        // Kardex: movimientos de un repuesto en una sucursal, en orden cronológico.
        b.HasIndex(x => new { x.BranchId, x.PartId, x.MovedAt });
        b.HasIndex(x => x.WorkOrderId);
    }
}

public class LaborServiceConfiguration : IEntityTypeConfiguration<LaborService>
{
    public void Configure(EntityTypeBuilder<LaborService> b)
    {
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Category).HasMaxLength(80);

        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
