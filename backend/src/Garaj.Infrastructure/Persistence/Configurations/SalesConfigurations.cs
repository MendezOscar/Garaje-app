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

        b.Property(x => x.FiscalNumber).HasMaxLength(30);
        b.Property(x => x.FiscalCai).HasMaxLength(50);
        b.Property(x => x.FiscalRangeText).HasMaxLength(80);
        b.Property(x => x.CustomerTaxId).HasMaxLength(50);

        b.Ignore(x => x.IsFiscal);

        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();

        // Único entre los que tienen número fiscal: si dos cajas emiten a la vez, la segunda
        // falla en la base en lugar de repetir un correlativo del SAR.
        b.HasIndex(x => new { x.TenantId, x.FiscalNumber })
            .IsUnique()
            .HasFilter("fiscal_number IS NOT NULL");

        // Índice de los reportes: ingresos de una sucursal en un rango de fechas.
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.SaleDate });
        b.HasIndex(x => x.WorkOrderId);
    }
}

public class SalePaymentConfiguration : IEntityTypeConfiguration<SalePayment>
{
    public void Configure(EntityTypeBuilder<SalePayment> b)
    {
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);

        b.HasOne(x => x.Sale)
            .WithMany(s => s.Payments)
            .HasForeignKey(x => x.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        // El saldo de una venta se calcula sumando por aquí, y el corte de caja del día
        // agrupa por fecha de pago.
        b.HasIndex(x => x.SaleId);
        b.HasIndex(x => new { x.TenantId, x.PaidAt });
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

public class FiscalRangeConfiguration : IEntityTypeConfiguration<FiscalRange>
{
    public void Configure(EntityTypeBuilder<FiscalRange> b)
    {
        b.Property(x => x.Cai).HasMaxLength(50).IsRequired();
        b.Property(x => x.EstablishmentCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.PointOfSaleCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.DocumentType).HasMaxLength(2).IsRequired();

        b.Ignore(x => x.Remaining);
        b.Ignore(x => x.RangeText);

        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);

        // Un solo rango activo por sucursal: es el que emite, y tener dos dejaría el
        // correlativo a suerte de cuál se lea primero.
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.IsActive })
            .IsUnique()
            .HasFilter("is_active");
    }
}
