using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(25).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.DocumentId).HasMaxLength(30);
        b.Property(x => x.TaxId).HasMaxLength(50);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Notes).HasMaxLength(1000);

        // La búsqueda por teléfono es la vía rápida en mostrador y la clave del link de WhatsApp.
        b.HasIndex(x => new { x.TenantId, x.Phone });
        b.HasIndex(x => new { x.TenantId, x.FullName });
        b.HasIndex(x => x.AppUserId);
    }
}

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.Property(x => x.Brand).HasMaxLength(80).IsRequired();
        b.Property(x => x.Model).HasMaxLength(80).IsRequired();
        b.Property(x => x.Plate).HasMaxLength(15);
        b.Property(x => x.Vin).HasMaxLength(25);
        b.Property(x => x.Color).HasMaxLength(40);
        b.Property(x => x.EngineNumber).HasMaxLength(40);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.Customer)
            .WithMany(c => c.Vehicles)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // La placa se guarda normalizada (mayúsculas, sin espacios) para que la búsqueda sea exacta.
        b.HasIndex(x => new { x.TenantId, x.Plate });
        b.HasIndex(x => x.CustomerId);
    }
}
