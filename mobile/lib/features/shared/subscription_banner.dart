import 'package:flutter/material.dart';

import '../../core/models/current_user.dart';

/// La franja del cobro. No tapa nada ni se puede cerrar: con la mensualidad vencida el taller
/// tiene que enterarse, y estando al día no se pinta.
class SubscriptionBanner extends StatelessWidget {
  const SubscriptionBanner({required this.info, super.key});

  final SubscriptionInfo info;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    // El acuerdo de pago se pinta en gris: es un recordatorio, no una alarma.
    final color = info.agreementThrough != null
        ? scheme.outline
        : info.canWrite
            ? const Color(0xFFB26A00)
            : scheme.error;

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 10),
      color: color.withValues(alpha: 0.10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(info.canWrite ? Icons.info_outline : Icons.lock_outline,
              size: 18, color: color),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              info.message,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: color,
                    fontWeight: info.canWrite ? FontWeight.normal : FontWeight.w600,
                  ),
            ),
          ),
        ],
      ),
    );
  }
}
