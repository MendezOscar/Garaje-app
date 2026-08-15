using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class JobTemplateConfiguration : IEntityTypeConfiguration<JobTemplate>
{
    public void Configure(EntityTypeBuilder<JobTemplate> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);

        // Dos «Cambio de aceite» en la misma lista no se distinguen al elegir, y el técnico
        // aplicaría el que no era.
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();

        // La lista se pide siempre así: los activos, el más usado arriba.
        b.HasIndex(x => new { x.TenantId, x.IsActive, x.UsageCount });
    }
}

public class JobTemplateTaskConfiguration : IEntityTypeConfiguration<JobTemplateTask>
{
    public void Configure(EntityTypeBuilder<JobTemplateTask> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);

        b.HasOne(x => x.JobTemplate)
            .WithMany(t => t.Tasks)
            .HasForeignKey(x => x.JobTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.JobTemplateId, x.Sequence });
    }
}

public class JobTemplatePartConfiguration : IEntityTypeConfiguration<JobTemplatePart>
{
    public void Configure(EntityTypeBuilder<JobTemplatePart> b)
    {
        b.Property(x => x.Description).HasMaxLength(200);

        b.HasOne(x => x.JobTemplate)
            .WithMany(t => t.Parts)
            .HasForeignKey(x => x.JobTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        // Opcional: una línea manual no apunta a ningún repuesto del catálogo. Restrict para
        // que dar de baja un repuesto no borre en silencio la línea de una plantilla.
        b.HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.JobTemplateId);
    }
}
