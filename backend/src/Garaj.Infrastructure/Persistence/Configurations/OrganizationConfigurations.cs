using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.LegalName).HasMaxLength(200);
        b.Property(x => x.TaxId).HasMaxLength(50);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.DefaultPhoneCountryCode).HasMaxLength(5).IsRequired();

        // Con valor por omisión en la base: los talleres que ya existen quedan en
        // vehículos al aplicar la migración, sin un UPDATE a mano.
        b.Property(x => x.WorksWith).HasDefaultValue(WorkshopKind.Vehicles);

        // Verdadero por omisión: el precio con el impuesto adentro es lo que hace casi todo
        // taller, y es lo que ya tenían los que existían antes de poder elegirlo.
        b.Property(x => x.PricesIncludeTax).HasDefaultValue(true);
        b.Property(x => x.LogoStorageKey).HasMaxLength(500);

        b.Property(x => x.PlanName).HasMaxLength(60);
        b.Property(x => x.UnblockNote).HasMaxLength(300);

        // La lista del panel de plataforma se pide siempre así: lo que vence primero, arriba.
        b.HasIndex(x => x.PaidThrough);

        // El enlace público de citas se resuelve por este token, sin sesión y sin tenant.
        b.HasIndex(x => x.BookingToken).IsUnique();
    }
}

public class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> b)
    {
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Method).HasMaxLength(40);
        b.Property(x => x.Reference).HasMaxLength(80);
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.VoidReason).HasMaxLength(300);

        // Cascade: si algún día se borra un taller de verdad, su historial de cobro se va con él.
        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Se lee de una sola forma: los pagos de un taller, el más reciente arriba.
        b.HasIndex(x => new { x.TenantId, x.PaidOn });
    }
}

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Phone).HasMaxLength(30);

        b.HasOne(x => x.Tenant)
            .WithMany(t => t.Branches)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // El código de sucursal prefija los correlativos de orden, cotización y venta.
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> b)
    {
        b.ToTable("user_branches");
        b.HasKey(x => new { x.UserId, x.BranchId });

        b.HasOne(x => x.User)
            .WithMany(u => u.Branches)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => x.CustomerId);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.Property(x => x.TokenHash).HasMaxLength(100).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(100);
        b.Property(x => x.CreatedByIp).HasMaxLength(60);
        b.Property(x => x.DeviceInfo).HasMaxLength(300);

        b.Ignore(x => x.IsActive);

        b.HasIndex(x => x.TokenHash).IsUnique();

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.LegalName).HasMaxLength(200);
        b.Property(x => x.TaxId).HasMaxLength(20);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.InvoicePrefix).HasMaxLength(8).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
    }
}

public class PlatformFiscalRangeConfiguration : IEntityTypeConfiguration<PlatformFiscalRange>
{
    public void Configure(EntityTypeBuilder<PlatformFiscalRange> b)
    {
        b.Property(x => x.Cai).HasMaxLength(50).IsRequired();
        b.Property(x => x.EstablishmentCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.PointOfSaleCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.DocumentType).HasMaxLength(2).IsRequired();

        // Se busca siempre igual: el rango vigente para emitir.
        b.HasIndex(x => x.IsActive);
    }
}

public class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> b)
    {
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.CustomerLegalName).HasMaxLength(200);
        b.Property(x => x.CustomerTaxId).HasMaxLength(20);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.FiscalCai).HasMaxLength(50);
        b.Property(x => x.FiscalRangeText).HasMaxLength(60);
        b.Property(x => x.VoidReason).HasMaxLength(300);

        // Dos documentos con el mismo número no se pueden distinguir, y uno de los dos es el
        // que el SAR no va a encontrar.
        b.HasIndex(x => x.Number).IsUnique();

        // Un pago, un documento: emitir dos veces por el mismo depósito es facturar de más.
        b.HasIndex(x => x.SubscriptionPaymentId).IsUnique();

        b.HasIndex(x => new { x.TenantId, x.IssuedOn });

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<SubscriptionPayment>()
            .WithMany()
            .HasForeignKey(x => x.SubscriptionPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SubscriptionInvoiceLineConfiguration : IEntityTypeConfiguration<SubscriptionInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoiceLine> b)
    {
        b.Property(x => x.Description).HasMaxLength(200).IsRequired();

        b.HasOne(x => x.SubscriptionInvoice)
            .WithMany(i => i.Lines)
            .HasForeignKey(x => x.SubscriptionInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.SubscriptionInvoiceId);
    }
}
