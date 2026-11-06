using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class AssistantCallConfiguration : IEntityTypeConfiguration<AssistantCall>
{
    public void Configure(EntityTypeBuilder<AssistantCall> b)
    {
        b.Property(x => x.Model).HasMaxLength(60).IsRequired();
        b.Property(x => x.Error).HasMaxLength(300);

        // El costo se cuenta en dólares con seis decimales: una llamada cuesta céntimos, y
        // con dos decimales todas redondearían a cero y el techo nunca llegaría.
        b.Property(x => x.CostUsd).HasPrecision(12, 6);

        // Sobre este índice se cuentan los tres techos, que se consultan antes de cada
        // llamada: por taller y por fecha.
        b.HasIndex(x => new { x.TenantId, x.CreatedAt });

        // El techo global de la plataforma mira el gasto de todos, sin taller de por medio.
        b.HasIndex(x => x.CreatedAt);
    }
}

public class AssistantProposalConfiguration : IEntityTypeConfiguration<AssistantProposal>
{
    public void Configure(EntityTypeBuilder<AssistantProposal> b)
    {
        // Sin tope de largo: es el envío entero y la respuesta entera. Recortarlos sería
        // perder justo lo que se guarda para poder entender una propuesta que salió mal.
        b.Property(x => x.Request).IsRequired();
        b.Property(x => x.Response).IsRequired();

        b.HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.TenantId, x.WorkOrderId, x.CreatedAt });
    }
}

public class BusinessReadingConfiguration : IEntityTypeConfiguration<BusinessReading>
{
    public void Configure(EntityTypeBuilder<BusinessReading> b)
    {
        b.Property(x => x.Data).IsRequired();
        b.Property(x => x.Content).IsRequired();

        // La última primero, que es la que se abre. Y una sola por periodo y tipo: pedirla dos
        // veces el mismo lunes gastaría dos llamadas para decir lo mismo.
        b.HasIndex(x => new { x.TenantId, x.Kind, x.PeriodTo }).IsUnique();
    }
}

public class AssistantConversationConfiguration : IEntityTypeConfiguration<AssistantConversation>
{
    public void Configure(EntityTypeBuilder<AssistantConversation> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();

        b.HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.TenantId, x.WorkOrderId, x.CreatedAt });
    }
}

public class AssistantMessageConfiguration : IEntityTypeConfiguration<AssistantMessage>
{
    public void Configure(EntityTypeBuilder<AssistantMessage> b)
    {
        b.Property(x => x.Content).IsRequired();

        b.HasOne(x => x.Conversation)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // En el orden en que se dijeron, que es como se lee una conversación.
        b.HasIndex(x => new { x.ConversationId, x.CreatedAt });
    }
}
