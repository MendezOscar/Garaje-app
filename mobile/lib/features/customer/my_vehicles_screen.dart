import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/quote_repository.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/quote.dart';
import '../../core/models/work_order.dart';
import '../../core/theme/garaj_brand.dart';
import '../notifications/notifications_screen.dart';
import '../reports/reports_screen.dart' show money;
import '../shared/tenant_logo.dart';

/// El inicio del Cliente: dónde está su vehículo y qué le toca a él.
///
/// Antes abría una bandeja de órdenes con folios, buscador y estados internos —«Esperando
/// aprobación»—, como si administrara el taller. Entra dos o tres veces por reparación y con
/// una sola pregunta, así que aquí lo primero es lo único que lo detiene todo —el presupuesto
/// sin responder— y después su vehículo, dicho en palabras.
class MyVehiclesScreen extends ConsumerWidget {
  const MyVehiclesScreen({required this.onVerHistorial, super.key});

  /// Ir al historial de un vehículo es cambiar de pestaña: lo decide el armazón.
  final void Function(String vehicleId) onVerHistorial;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    if (auth is! AuthSignedIn) return const SizedBox.shrink();

    final ordenes = ref.watch(openOrdersProvider);
    final vehiculos = ref.watch(vehicleOptionsProvider(''));
    final cotizaciones = ref.watch(myQuotesProvider);

    final enTaller = ordenes.value ?? const <WorkOrderListItem>[];
    final enTallerIds = enTaller.map((o) => o.vehicleId).toSet();
    final enCasa = (vehiculos.value ?? const <VehicleOption>[])
        .where((v) => !enTallerIds.contains(v.id))
        .toList();

    final pendientes =
        (cotizaciones.value ?? const <Quote>[]).where((q) => q.canRespond).toList();

    return Scaffold(
      appBar: AppBar(
        title: Row(
          children: [
            const TenantLogo(),
            const SizedBox(width: 8),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(auth.user.tenantName, overflow: TextOverflow.ellipsis),
                  Text(
                    auth.user.fullName,
                    style: Theme.of(context).textTheme.bodySmall,
                    overflow: TextOverflow.ellipsis,
                  ),
                ],
              ),
            ),
          ],
        ),
        actions: const [NotificationBell()],
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref
            ..invalidate(openOrdersProvider)
            ..invalidate(vehicleOptionsProvider(''))
            ..invalidate(myQuotesProvider);
        },
        child: ordenes.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(
            children: [
              const SizedBox(height: 100),
              Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Column(
                    children: [
                      Text(
                        apiErrorMessage(e, 'No se pudieron cargar sus vehículos.'),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 16),
                      FilledButton.tonal(
                        onPressed: () => ref.invalidate(openOrdersProvider),
                        child: const Text('Reintentar'),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
          data: (_) => ListView(
            padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
            children: [
              for (final quote in pendientes) _PendienteCard(quote: quote),
              for (final order in enTaller) _EnTallerCard(order: order),
              for (final vehiculo in enCasa)
                _EnCasaCard(
                  vehiculo: vehiculo,
                  onTap: () => onVerHistorial(vehiculo.id),
                ),
              if (enTaller.isEmpty && enCasa.isEmpty)
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 48, horizontal: 12),
                  child: Text(
                    'Su taller todavía no le ha registrado un vehículo. Pida una cita y lo '
                    'registran al recibirlo.',
                    textAlign: TextAlign.center,
                  ),
                ),
              const SizedBox(height: 4),
              FilledButton.icon(
                onPressed: () => context.push('/nueva-cita'),
                icon: const Icon(Icons.add),
                label: const Text('Pedir una cita'),
              ),
              const SizedBox(height: 10),
              Text(
                'El taller le avisa cada vez que su vehículo cambia de estado.',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Lo único que le toca al Cliente: responder el presupuesto. Va primero porque hasta que
/// conteste, el taller no puede seguir.
class _PendienteCard extends StatelessWidget {
  const _PendienteCard({required this.quote});

  final Quote quote;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Card(
        shape: RoundedRectangleBorder(
          side: BorderSide(color: Color.lerp(theme.dividerColor, GarajColors.warning, 0.55)!),
          borderRadius: const BorderRadius.all(Radius.circular(10)),
        ),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  const Icon(Icons.warning_amber_rounded,
                      size: 18, color: GarajColors.warning),
                  const SizedBox(width: 8),
                  Text(
                    'LE TOCA A USTED',
                    style: theme.textTheme.labelSmall?.copyWith(letterSpacing: 0.6),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Text(
                'El taller le mandó un presupuesto de ${money(quote.total, quote.currency)} '
                'y espera su respuesta para seguir.',
                style: theme.textTheme.bodyLarge,
              ),
              const SizedBox(height: 4),
              Text(
                [
                  if (quote.vehicleLabel != null) quote.vehicleLabel!,
                  quote.number,
                  if (quote.validUntil != null) 'vale hasta ${_fecha(quote.validUntil!)}',
                ].join(' · '),
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: 12),
              FilledButton(
                onPressed: () => context.push('/presupuesto/${quote.id}'),
                child: const Text('Ver el presupuesto'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Un vehículo que está en el taller ahora mismo.
class _EnTallerCard extends StatelessWidget {
  const _EnTallerCard({required this.order});

  final WorkOrderListItem order;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final listo = order.status == WorkOrderStatus.ready;

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(order.vehicleLabel, style: theme.textTheme.titleMedium),
                  ),
                  if (order.plate != null) ...[
                    _Placa(plate: order.plate!),
                    const SizedBox(width: 8),
                  ],
                  _Etiqueta(
                    texto: listo ? 'Listo' : 'En el taller',
                    color: listo ? GarajColors.success : GarajColors.warning,
                  ),
                ],
              ),
              const SizedBox(height: 8),
              // El estado en palabras. Los nueve estados internos del taller no le dicen
              // nada a quien solo quiere saber si ya puede pasar por su carro.
              Text(_enPalabras(order.status), style: theme.textTheme.bodyLarge),
              if (order.taskCount > 0) ...[
                const SizedBox(height: 10),
                Row(
                  children: [
                    Expanded(
                      child: LinearProgressIndicator(
                        value: order.tasksDone / order.taskCount,
                        minHeight: 4,
                      ),
                    ),
                    const SizedBox(width: 8),
                    Text(
                      '${order.tasksDone}/${order.taskCount}',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ],
              const SizedBox(height: 6),
              Text(
                [
                  'entró el ${_fecha(order.openedAt)}',
                  if (order.promisedAt != null)
                    'lo prometieron para el ${_fecha(order.promisedAt!)}',
                ].join(' · '),
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: 12),
              OutlinedButton(
                onPressed: () => context.push('/ordenes/${order.id}'),
                style: OutlinedButton.styleFrom(
                  minimumSize: const Size.fromHeight(48),
                  shape: const RoundedRectangleBorder(
                    borderRadius: BorderRadius.all(Radius.circular(6)),
                  ),
                  side: BorderSide(color: theme.dividerColor),
                ),
                child: const Text('Ver el avance'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _EnCasaCard extends StatelessWidget {
  const _EnCasaCard({required this.vehiculo, required this.onTap});

  final VehicleOption vehiculo;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Card(
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(vehiculo.label, style: theme.textTheme.titleMedium),
                      Text(
                        vehiculo.mileage == null
                            ? 'Ver su historial'
                            : '${vehiculo.mileage} km · ver su historial',
                        style: theme.textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
                const _Etiqueta(texto: 'En casa', color: null),
                const SizedBox(width: 4),
                Icon(Icons.chevron_right,
                    size: 18, color: theme.colorScheme.onSurfaceVariant),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _Etiqueta extends StatelessWidget {
  const _Etiqueta({required this.texto, required this.color});

  final String texto;

  /// Null se pinta en gris: «en casa» no es un aviso de nada.
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final base = color ?? theme.colorScheme.onSurfaceVariant;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: base.withValues(alpha: color == null ? 0.10 : 0.18),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        texto,
        style: theme.textTheme.labelSmall?.copyWith(
          color: color == null ? theme.colorScheme.onSurfaceVariant : base,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

class _Placa extends StatelessWidget {
  const _Placa({required this.plate});

  final String plate;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
      decoration: BoxDecoration(
        border: Border.all(color: theme.dividerColor),
        borderRadius: BorderRadius.circular(3),
      ),
      child: Text(plate, style: theme.textTheme.labelSmall),
    );
  }
}

/// El estado del taller, contado como se lo diría el mecánico en el mostrador.
String _enPalabras(WorkOrderStatus status) => switch (status) {
      WorkOrderStatus.received => 'Ya lo recibieron en el taller.',
      WorkOrderStatus.diagnosing => 'Están revisando qué tiene.',
      WorkOrderStatus.waitingApproval => 'Esperando que usted apruebe el presupuesto.',
      WorkOrderStatus.waitingParts => 'Esperando que llegue un repuesto.',
      WorkOrderStatus.inProgress => 'Lo están reparando.',
      WorkOrderStatus.testing => 'Lo están probando antes de entregarlo.',
      WorkOrderStatus.ready => 'Ya está listo: puede pasar a recogerlo.',
      WorkOrderStatus.delivered => 'Entregado.',
      WorkOrderStatus.cancelled => 'El trabajo se canceló.',
    };

String _fecha(DateTime value) {
  const meses = [
    'ene', 'feb', 'mar', 'abr', 'may', 'jun',
    'jul', 'ago', 'sep', 'oct', 'nov', 'dic',
  ];
  final local = value.toLocal();
  return '${local.day} de ${meses[local.month - 1]}';
}
