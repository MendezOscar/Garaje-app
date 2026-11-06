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
        b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);

        // Un requerimiento es de un vehículo o de un equipo, nunca de los dos ni de ninguno.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_service_requests_vehiculo_o_equipo",
            "(vehicle_id IS NULL) <> (equipment_id IS NULL)"));

        // La bandeja del Dueño lista los pendientes de una sucursal ordenados por fecha.
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.Status, x.CreatedAt });
        b.HasIndex(x => x.VehicleId);
        b.HasIndex(x => x.EquipmentId);
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
        b.HasOne(x => x.Equipment).WithMany(e => e.WorkOrders).HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);

        // Lo mismo que en el requerimiento: uno de los dos, y solo uno.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_work_orders_vehiculo_o_equipo",
            "(vehicle_id IS NULL) <> (equipment_id IS NULL)"));

        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();

        // El enlace de seguimiento se resuelve por este token y sin filtro de tenant: sin
        // índice sería un recorrido de toda la tabla en cada apertura.
        b.HasIndex(x => x.PublicToken).IsUnique();

        // La lista de recordatorios pregunta justo por esto: a quién le toca servicio pronto.
        b.HasIndex(x => new { x.TenantId, x.NextServiceAt });

        // Kanban del Dueño y bandeja del Técnico.
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.AssignedTechnicianId, x.Status });
        b.HasIndex(x => x.VehicleId);
        b.HasIndex(x => x.EquipmentId);
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
        b.Property(x => x.SupplierName).HasMaxLength(120);

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

public class WorkOrderTimeEntryConfiguration : IEntityTypeConfiguration<WorkOrderTimeEntry>
{
    public void Configure(EntityTypeBuilder<WorkOrderTimeEntry> b)
    {
        b.HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Se lee de dos formas: los tramos de una orden, y los de una persona en un rango
        // —que es el reporte del dueño—.
        b.HasIndex(x => x.WorkOrderId);
        b.HasIndex(x => new { x.UserId, x.StartedAt });

        // El tramo abierto se busca en cada «Empezar» para cerrar el anterior.
        b.HasIndex(x => new { x.TenantId, x.UserId, x.EndedAt });
    }
}

public class VehicleReceptionConfiguration : IEntityTypeConfiguration<VehicleReception>
{
    public void Configure(EntityTypeBuilder<VehicleReception> b)
    {
        b.Property(x => x.Damages).HasMaxLength(2000);
        b.Property(x => x.Belongings).HasMaxLength(2000);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.DeliveredByName).HasMaxLength(200);
        b.Property(x => x.SignatureStorageKey).HasMaxLength(300);

        b.HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ServiceRequest)
            .WithMany()
            .HasForeignKey(x => x.ServiceRequestId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // Una por orden y una por requerimiento: recibir el vehículo pasa una sola vez. Los
        // índices son filtrados porque la otra llave va en null hasta que se aprueba.
        b.HasIndex(x => x.WorkOrderId).IsUnique().HasFilter("work_order_id IS NOT NULL");
        b.HasIndex(x => x.ServiceRequestId).IsUnique().HasFilter("service_request_id IS NOT NULL");
    }
}

public class VehicleScanConfiguration : IEntityTypeConfiguration<VehicleScan>
{
    public void Configure(EntityTypeBuilder<VehicleScan> b)
    {
        b.Property(x => x.Vin).HasMaxLength(20);
        b.Property(x => x.DeviceName).HasMaxLength(100);
        b.Property(x => x.Protocol).HasMaxLength(60);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // La galería de la orden pide las lecturas de la orden, de la más reciente a la vieja.
        b.HasIndex(x => new { x.TenantId, x.WorkOrderId, x.ScannedAt });

        // «Qué le ha salido a este carro»: el historial del vehículo, que es lo que se mira
        // cuando vuelve con lo mismo tres meses después.
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.ScannedAt });
    }
}

public class VehicleScanCodeConfiguration : IEntityTypeConfiguration<VehicleScanCode>
{
    public void Configure(EntityTypeBuilder<VehicleScanCode> b)
    {
        b.Property(x => x.Code).HasMaxLength(10).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);

        b.HasOne(x => x.Scan)
            .WithMany(x => x.Codes)
            .HasForeignKey(x => x.VehicleScanId)
            .OnDelete(DeleteBehavior.Cascade);

        // La razón de ser de toda esta tabla: buscar «todos los carros que reportaron P0301»
        // sin recorrer las lecturas una por una.
        b.HasIndex(x => new { x.TenantId, x.Code });

        // El mismo código no se repite dos veces en la misma lectura con el mismo estado:
        // el adaptador los devuelve así cuando una falla la reportan dos módulos distintos.
        b.HasIndex(x => new { x.VehicleScanId, x.Code, x.Status }).IsUnique();
    }
}
