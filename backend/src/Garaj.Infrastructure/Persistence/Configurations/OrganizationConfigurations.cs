using Garaj.Domain.Entities;
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
        b.Property(x => x.LogoStorageKey).HasMaxLength(500);

        b.Property(x => x.PlanName).HasMaxLength(60);
        b.Property(x => x.UnblockNote).HasMaxLength(300);

        // La lista del panel de plataforma se pide siempre así: lo que vence primero, arriba.
        b.HasIndex(x => x.PaidThrough);
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
