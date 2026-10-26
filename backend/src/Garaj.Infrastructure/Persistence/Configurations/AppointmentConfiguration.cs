using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContactPhone).HasMaxLength(30).IsRequired();
        b.Property(x => x.VehicleNote).HasMaxLength(300);
        b.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        b.Property(x => x.ResponseNote).HasMaxLength(500);

        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);

        // La agenda pregunta siempre lo mismo: qué hay en esta sucursal para estos días.
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.Day, x.Status });

        // La página del cliente se resuelve por token y sin filtro de tenant: sin índice sería
        // un recorrido de toda la tabla en cada apertura.
        b.HasIndex(x => x.PublicToken).IsUnique();

        // Tope de citas por número al día: se cuenta por teléfono, no por cita.
        b.HasIndex(x => new { x.TenantId, x.ContactPhone });
    }
}
