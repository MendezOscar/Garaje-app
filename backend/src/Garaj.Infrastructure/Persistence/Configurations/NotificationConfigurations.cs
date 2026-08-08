using Garaj.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garaj.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Body).HasMaxLength(500).IsRequired();

        // La campana pide siempre lo mismo: lo del usuario, lo más nuevo primero. Y el
        // contador de no leídos filtra por ReadAt, así que entra en el mismo índice.
        b.HasIndex(x => new { x.RecipientUserId, x.ReadAt, x.CreatedAt });
    }
}

public class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> b)
    {
        b.Property(x => x.Token).HasMaxLength(500).IsRequired();

        // Único en todo el sistema, no por taller: el token identifica al aparato, y si un
        // teléfono cambia de dueño la fila se reasigna en lugar de duplicarse. Sin esto, el
        // usuario anterior seguiría recibiendo los avisos del nuevo.
        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => x.UserId);
    }
}
