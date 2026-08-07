using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> b)
    {
        b.Property(x => x.Number).HasMaxLength(30).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.CustomerResponseNote).HasMaxLength(1000);

        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();

        // El link público resuelve la cotización solo por este token, así que debe ser único
        // globalmente: la consulta corre sin filtro de tenant.
        b.HasIndex(x => x.PublicToken).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
    }
}

public class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> b)
    {
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();

        b.HasOne(x => x.Quote)
            .WithMany(q => q.Lines)
            .HasForeignKey(x => x.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.QuoteId, x.Sequence });
    }
}

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.Property(x => x.Number).HasMaxLength(30).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.VoidReason).HasMaxLength(500);

        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();

        // Índice de los reportes: ingresos de una sucursal en un rango de fechas.
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.SaleDate });
        b.HasIndex(x => x.WorkOrderId);
    }
}

public class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();

        b.HasOne(x => x.Sale)
            .WithMany(s => s.Lines)
            .HasForeignKey(x => x.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        // El desglose repuestos vs mano de obra agrupa por este par.
        b.HasIndex(x => new { x.SaleId, x.LineType });
    }
}
