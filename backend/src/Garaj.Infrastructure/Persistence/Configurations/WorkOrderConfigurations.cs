using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> b)
    {
        b.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        b.Property(x => x.ReportedSymptoms).HasMaxLength(2000);
        b.Property(x => x.RejectionReason).HasMaxLength(500);

        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);

        // La bandeja del Dueño lista los pendientes de una sucursal ordenados por fecha.
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.Status, x.CreatedAt });
        b.HasIndex(x => x.VehicleId);
    }
}

public class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> b)
    {
        b.Property(x => x.Number).HasMaxLength(30).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Diagnosis).HasMaxLength(4000);

        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vehicle).WithMany(v => v.WorkOrders).HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();

        // El enlace de seguimiento se resuelve por este token y sin filtro de tenant: sin
        // índice sería un recorrido de toda la tabla en cada apertura.
        b.HasIndex(x => x.PublicToken).IsUnique();

        // Kanban del Dueño y bandeja del Técnico.
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.AssignedTechnicianId, x.Status });
        b.HasIndex(x => x.VehicleId);
    }
}

public class WorkOrderTaskConfiguration : IEntityTypeConfiguration<WorkOrderTask>
{
    public void Configure(EntityTypeBuilder<WorkOrderTask> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.TechnicianNotes).HasMaxLength(2000);

        b.HasOne(x => x.WorkOrder)
            .WithMany(w => w.Tasks)
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.WorkOrderId, x.Sequence });
    }
}

public class WorkOrderPartConfiguration : IEntityTypeConfiguration<WorkOrderPart>
{
    public void Configure(EntityTypeBuilder<WorkOrderPart> b)
    {
        b.HasOne(x => x.WorkOrder)
            .WithMany(w => w.Parts)
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.Description).HasMaxLength(200);

        // Opcional: una línea manual no apunta a ningún repuesto del catálogo.
        b.HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.WorkOrderId);
    }
}

public class WorkOrderStatusHistoryConfiguration : IEntityTypeConfiguration<WorkOrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<WorkOrderStatusHistory> b)
    {
        b.ToTable("work_order_status_history");
        b.Property(x => x.Note).HasMaxLength(1000);

        b.HasOne(x => x.WorkOrder)
            .WithMany(w => w.StatusHistory)
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.WorkOrderId, x.ChangedAt });
    }
}

public class MediaAttachmentConfiguration : IEntityTypeConfiguration<MediaAttachment>
{
    public void Configure(EntityTypeBuilder<MediaAttachment> b)
    {
        b.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.ThumbnailKey).HasMaxLength(500);
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(300);
        b.Property(x => x.Caption).HasMaxLength(500);

        // Relación polimórfica: sin FK, se resuelve por (OwnerType, OwnerId).
        b.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId });
        b.HasIndex(x => x.StorageKey).IsUnique();
    }
}
