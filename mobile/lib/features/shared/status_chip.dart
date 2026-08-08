import 'package:flutter/material.dart';

import '../../core/models/work_order.dart';
import '../../core/theme/garaj_brand.dart';

/// El color dice si la orden avanza, está detenida esperando a alguien, o ya terminó.
/// Con nueve estados, un color por estado sería ilegible en la pantalla de un teléfono.
class StatusChip extends StatelessWidget {
  const StatusChip({required this.status, super.key});

  final WorkOrderStatus status;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final dark = Theme.of(context).brightness == Brightness.dark;

    // Los colores salen de la marca y no del esquema de Material: ámbar es "esperando a
    // alguien" y verde es "terminado" en toda la aplicación, y el tono que Material deriva
    // de la semilla azul no dice ninguna de las dos cosas.
    final tone = switch (status) {
      _ when status == WorkOrderStatus.cancelled => null,
      _ when status.isBlocked =>
        dark ? GarajColors.warningLight : GarajColors.warning,
      WorkOrderStatus.ready || WorkOrderStatus.delivered =>
        dark ? GarajColors.successLight : GarajColors.success,
      _ => dark ? GarajColors.brandLight : GarajColors.brand,
    };

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: tone == null
            ? scheme.surfaceContainerHighest
            : tone.withValues(alpha: 0.16),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        status.label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
              color: tone ?? scheme.onSurfaceVariant,
              fontWeight: FontWeight.w600,
            ),
      ),
    );
  }
}
