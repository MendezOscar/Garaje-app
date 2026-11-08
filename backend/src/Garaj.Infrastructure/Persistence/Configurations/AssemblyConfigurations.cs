using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class AssemblyContractorConfiguration : IEntityTypeConfiguration<AssemblyContractor>
{
    public void Configure(EntityTypeBuilder<AssemblyContractor> b)
    {
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);

        // Dos contratistas con el mismo nombre en el mismo taller no son dos contratistas:
        // es uno cargado dos veces, y la tarifa quedaría partida entre los dos.
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();

        // Restrict: una sucursal con un contrato colgando no se borra sin decidir qué pasa
        // con lo ya facturado.
        b.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AssemblyModelConfiguration : IEntityTypeConfiguration<AssemblyModel>
{
    public void Configure(EntityTypeBuilder<AssemblyModel> b)
    {
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.VinPrefix).HasMaxLength(10);

        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();

        // Por aquí se busca el modelo de una serie recién escaneada, antes de cada registro.
        b.HasIndex(x => new { x.TenantId, x.VinPrefix });
    }
}

public class AssemblyRateConfiguration : IEntityTypeConfiguration<AssemblyRate>
{
    public void Configure(EntityTypeBuilder<AssemblyRate> b)
    {
        b.HasOne(x => x.Contractor)
            .WithMany(x => x.Rates)
            .HasForeignKey(x => x.ContractorId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Model)
            .WithMany()
            .HasForeignKey(x => x.ModelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Una tarifa por contratista y modelo. Dos filas para el mismo par significarían dos
        // precios para el mismo trabajo, y cuál gana dependería del orden de la consulta.
        b.HasIndex(x => new { x.TenantId, x.ContractorId, x.ModelId }).IsUnique();
    }
}

public class AssemblyWarehouseConfiguration : IEntityTypeConfiguration<AssemblyWarehouse>
{
    public void Configure(EntityTypeBuilder<AssemblyWarehouse> b)
    {
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public class MotorcycleAssemblyConfiguration : IEntityTypeConfiguration<MotorcycleAssembly>
{
    public void Configure(EntityTypeBuilder<MotorcycleAssembly> b)
    {
        b.Property(x => x.Serial).HasMaxLength(25).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);

        b.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Contractor)
            .WithMany()
            .HasForeignKey(x => x.ContractorId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Model)
            .WithMany()
            .HasForeignKey(x => x.ModelId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // La regla que no puede ceder: una serie, una moto. Repetida es una moto cobrada dos
        // veces, y en la base porque el chequeo en el servicio no sobrevive a dos técnicos
        // registrando la misma a la vez desde dos bodegas.
        b.HasIndex(x => new { x.TenantId, x.Serial }).IsUnique();

        // Por donde se recorre de verdad: el periodo que se va a facturar, y el periodo de un
        // técnico para pagarle.
        b.HasIndex(x => new { x.TenantId, x.AssembledOn });
        b.HasIndex(x => new { x.TenantId, x.TechnicianUserId, x.AssembledOn });

        // Lo que falta por facturar y lo que falta por pagar: las dos preguntas con las que
        // se abre el mes.
        b.HasIndex(x => new { x.TenantId, x.BillingSettlementId });
        b.HasIndex(x => new { x.TenantId, x.PaySettlementId });
    }
}

public class AssemblySettlementConfiguration : IEntityTypeConfiguration<AssemblySettlement>
{
    public void Configure(EntityTypeBuilder<AssemblySettlement> b)
    {
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.CollectedReference).HasMaxLength(60);

        b.HasOne(x => x.Contractor)
            .WithMany()
            .HasForeignKey(x => x.ContractorId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.Kind, x.PeriodTo });
    }
}
