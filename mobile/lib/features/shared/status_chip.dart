import 'package:flutter/material.dart';

import '../../core/models/work_order.dart';

/// El color dice si la orden avanza, está detenida esperando a alguien, o ya terminó.
/// Con nueve estados, un color por estado sería ilegible en la pantalla de un teléfono.
class StatusChip extends StatelessWidget {
  const StatusChip({required this.status, super.key});

  final WorkOrderStatus status;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    final (background, foreground) = switch (status) {
      _ when status == WorkOrderStatus.cancelled => (
          scheme.surfaceContainerHighest,
          scheme.onSurfaceVariant
        ),
      _ when status.isBlocked => (scheme.tertiaryContainer, scheme.onTertiaryContainer),
      WorkOrderStatus.ready ||
      WorkOrderStatus.delivered =>
        (scheme.secondaryContainer, scheme.onSecondaryContainer),
      _ => (scheme.primaryContainer, scheme.onPrimaryContainer),
    };

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        status.label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(color: foreground),
      ),
    );
  }
}
