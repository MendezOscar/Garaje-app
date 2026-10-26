using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> b)
    {
        b.Property(x => x.Brand).HasMaxLength(80).IsRequired();
        b.Property(x => x.Model).HasMaxLength(80).IsRequired();
        b.Property(x => x.SerialNumber).HasMaxLength(60);
        b.Property(x => x.Tag).HasMaxLength(30);
        b.Property(x => x.Color).HasMaxLength(40);
        b.Property(x => x.Accessories).HasMaxLength(500);
        b.Property(x => x.Attributes).HasColumnType("jsonb");
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.Customer)
            .WithMany(c => c.Equipment)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.EquipmentType)
            .WithMany(t => t.Equipment)
            .HasForeignKey(x => x.EquipmentTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // La vía rápida del mostrador es el cliente; la serie y la etiqueta son el atajo
        // para cuando el aparato ya está en la bodega y hay que saber de quién es.
        b.HasIndex(x => x.CustomerId);
        b.HasIndex(x => new { x.TenantId, x.SerialNumber });
        b.HasIndex(x => new { x.TenantId, x.Tag });
    }
}

public class EquipmentTypeConfiguration : IEntityTypeConfiguration<EquipmentType>
{
    public void Configure(EntityTypeBuilder<EquipmentType> b)
    {
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Icon).HasMaxLength(40);

        // Dos categorías con el mismo nombre en el mismo taller son un error de dedo, y el
        // que escoge en la pantalla no tiene cómo distinguirlas.
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}
