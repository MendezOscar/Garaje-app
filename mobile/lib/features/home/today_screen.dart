import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/dashboard_repository.dart';
import '../../core/api/report_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/work_order.dart';
import '../../core/theme/garaj_brand.dart';
import '../notifications/notifications_screen.dart';
import '../reports/reports_screen.dart' show money;
import '../shared/subscription_banner.dart';
import '../shared/tenant_logo.dart';

/// El inicio del Dueño: cómo va el día del taller.
///
/// Antes esto vivía como una tarjeta encima de la bandeja de órdenes, y lo primero que se veía
/// al abrir la app era lo **facturado**. Pero la pregunta de quien abre el teléfono a media
/// tarde es cuánto **entró**, que no es lo mismo: una venta a crédito se factura hoy y se cobra
/// en quince días. Así que arriba va lo cobrado, con lo facturado al lado para comparar.
///
/// Todo lo demás sale de la misma respuesta de `/api/reports/dashboard` —incluido el patio por
/// estado—, así que la pantalla pide dos cosas: el resumen y el cierre de caja del día.
class TodayScreen extends ConsumerWidget {
  const TodayScreen({required this.onVerOrdenes, super.key});

  /// Llevar a la bandeja es cambiar de pestaña, no empujar una pantalla: lo decide el armazón.
  final VoidCallback onVerOrdenes;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    if (auth is! AuthSignedIn) return const SizedBox.shrink();

    final resumen = ref.watch(dashboardProvider);
    final caja = ref.watch(cashCloseProvider(null));

    return Scaffold(
      appBar: AppBar(
        title: Row(
          children: [
            const TenantLogo(),
            const SizedBox(width: 8),
            const Expanded(child: Text('Hoy')),
          ],
        ),
        actions: const [NotificationBell()],
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref
            ..invalidate(dashboardProvider)
            ..invalidate(cashCloseProvider(null))
            ..invalidate(remindersDueProvider);
        },
        child: ListView(
          padding: EdgeInsets.zero,
          children: [
            if (auth.user.subscription?.shouldWarn ?? false)
              SubscriptionBanner(info: auth.user.subscription!),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
              child: resumen.when(
                loading: () => const Padding(
                  padding: EdgeInsets.symmetric(vertical: 64),
                  child: Center(child: CircularProgressIndicator()),
                ),
                error: (e, _) => Padding(
                  padding: const EdgeInsets.symmetric(vertical: 48),
                  child: Column(
                    children: [
                      Text(
                        apiErrorMessage(e, 'No se pudo cargar el resumen del día.'),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 16),
                      FilledButton.tonal(
                        onPressed: () => ref.invalidate(dashboardProvider),
                        child: const Text('Reintentar'),
                      ),
                    ],
                  ),
                ),
                data: (d) => Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      auth.user.tenantName,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    const SizedBox(height: 10),
                    _MoneyCard(summary: d, cobrado: caja.value),
                    const SizedBox(height: 12),
                    _Tiles(summary: d, onVerOrdenes: onVerOrdenes),
                    const SizedBox(height: 12),
                    _Yard(summary: d, onVerOrdenes: onVerOrdenes),
                    const SizedBox(height: 12),
                    const _RemindersCard(),
                    const SizedBox(height: 16),
                    // Recibir un vehículo es lo que más se hace en el mostrador y hasta hoy
                    // estaba a dos pantallas de distancia.
                    FilledButton.icon(
                      onPressed: () => context.push('/nueva-cita'),
                      icon: const Icon(Icons.add),
                      label: const Text('Recibir vehículo'),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Lo cobrado del día contra lo facturado.
class _MoneyCard extends StatelessWidget {
  const _MoneyCard({required this.summary, required this.cobrado});

  final DashboardSummary summary;

  /// Null mientras el cierre de caja va en camino: la cifra grande aparece un instante después.
  final CashClose? cobrado;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final moneda = summary.currency;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('COBRADO HOY', style: _label(theme)),
                      Text(
                        cobrado == null ? '—' : money(cobrado!.total, moneda),
                        style: theme.textTheme.headlineMedium?.copyWith(
                          fontFamily: GarajFonts.mono,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ],
                  ),
                ),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text('FACTURADO', style: _label(theme)),
                    Text(
                      money(summary.today, moneda),
                      style: theme.textTheme.titleMedium
                          ?.copyWith(fontFamily: GarajFonts.mono),
                    ),
                  ],
                ),
              ],
            ),
            // Las dos cifras casi nunca cuadran, y sin explicarlo parece un error del sistema.
            if (cobrado != null && cobrado!.total != summary.today) ...[
              const SizedBox(height: 8),
              Text(
                'Lo cobrado y lo facturado no cuadran a diario: una venta a crédito se '
                'factura hoy y se cobra después.',
                style: theme.textTheme.bodySmall,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// Las cuatro cosas que piden atención, en dos por dos: se ven sin desplazar la pantalla.
class _Tiles extends StatelessWidget {
  const _Tiles({required this.summary, required this.onVerOrdenes});

  final DashboardSummary summary;
  final VoidCallback onVerOrdenes;

  @override
  Widget build(BuildContext context) {
    final d = summary;

    return Column(
      children: [
        Row(
          children: [
            Expanded(
              child: _Tile(
                title: 'POR ATENDER',
                value: '${d.pendingRequests}',
                foot: 'requerimientos',
                tone: d.pendingRequests > 0 ? _Tone.waiting : _Tone.plain,
                onTap: () => context.push('/requerimientos'),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: _Tile(
                title: 'ATRASADAS',
                value: '${d.lateWorkOrders}',
                foot: 'pasó la fecha',
                tone: d.lateWorkOrders > 0 ? _Tone.alarm : _Tone.plain,
                onTap: onVerOrdenes,
              ),
            ),
          ],
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            Expanded(
              child: _Tile(
                title: 'SIN RESPUESTA',
                value: '${d.quotesAwaitingResponse}',
                foot: 'cotizaciones',
                tone: d.quotesAwaitingResponse > 0 ? _Tone.waiting : _Tone.plain,
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: _Tile(
                title: 'VENCIDO',
                value: money(d.overdueReceivables, d.currency),
                foot: 'por cobrar',
                tone: d.overdueReceivables > 0 ? _Tone.alarm : _Tone.plain,
                onTap: () => context.push('/por-cobrar'),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

enum _Tone { plain, waiting, alarm }

class _Tile extends StatelessWidget {
  const _Tile({
    required this.title,
    required this.value,
    required this.foot,
    required this.tone,
    this.onTap,
  });

  final String title;
  final String value;
  final String foot;
  final _Tone tone;

  /// Sin destino la teja solo informa: de las cotizaciones sin respuesta no hay pantalla en el
  /// teléfono —se contestan desde la orden— y prometer un toque que no lleva a nada es peor.
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // En cero nada grita: la teja se queda en gris y el color queda libre para lo que sí urge.
    final color = switch (tone) {
      _Tone.plain => null,
      _Tone.waiting => GarajColors.warning,
      _Tone.alarm => theme.colorScheme.error,
    };

    return Card(
      clipBehavior: Clip.antiAlias,
      // El borde tira hacia el color del estado sin llegar a serlo: marca la teja de lejos y
      // deja el color pleno para la cifra.
      shape: RoundedRectangleBorder(
        side: BorderSide(
          color: color == null ? theme.dividerColor : Color.lerp(theme.dividerColor, color, 0.45)!,
        ),
        borderRadius: const BorderRadius.all(Radius.circular(10)),
      ),
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: _label(theme)),
              const SizedBox(height: 2),
              Text(
                value,
                style: theme.textTheme.titleLarge?.copyWith(color: color),
              ),
              Text(foot, style: theme.textTheme.bodySmall),
            ],
          ),
        ),
      ),
    );
  }
}

/// El patio: cuántas órdenes vivas hay en cada tramo del proceso.
///
/// Los nueve estados agrupados en cuatro renglones. Nueve cifras no se leen de un vistazo, y en
/// un taller pequeño la mayoría de los estados están casi siempre en cero.
class _Yard extends StatelessWidget {
  const _Yard({required this.summary, required this.onVerOrdenes});

  final DashboardSummary summary;
  final VoidCallback onVerOrdenes;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final carriles = <(String, int, Color)>[
      (
        'Entrando',
        summary.count([WorkOrderStatus.received, WorkOrderStatus.diagnosing]),
        theme.colorScheme.primary,
      ),
      (
        'En trabajo',
        summary.count([WorkOrderStatus.inProgress, WorkOrderStatus.testing]),
        theme.colorScheme.primary,
      ),
      (
        'Detenidas',
        summary.count([WorkOrderStatus.waitingApproval, WorkOrderStatus.waitingParts]),
        GarajColors.warning,
      ),
      (
        'Listas para entrega',
        summary.count([WorkOrderStatus.ready]),
        GarajColors.success,
      ),
    ];

    return Card(
      clipBehavior: Clip.antiAlias,
      child: Column(
        children: [
          for (final (nombre, cantidad, color) in carriles)
            InkWell(
              onTap: onVerOrdenes,
              child: Container(
                decoration: BoxDecoration(
                  border: nombre == carriles.last.$1
                      ? null
                      : Border(bottom: BorderSide(color: theme.dividerColor)),
                ),
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                child: Row(
                  children: [
                    Container(
                      width: 9,
                      height: 9,
                      decoration: BoxDecoration(color: color, shape: BoxShape.circle),
                    ),
                    const SizedBox(width: 12),
                    Expanded(child: Text(nombre)),
                    Text(
                      '$cantidad',
                      style: theme.textTheme.titleMedium
                          ?.copyWith(fontFamily: GarajFonts.mono),
                    ),
                    const SizedBox(width: 6),
                    Icon(Icons.chevron_right,
                        size: 18, color: theme.colorScheme.onSurfaceVariant),
                  ],
                ),
              ),
            ),
        ],
      ),
    );
  }
}

/// A cuántos vehículos les toca servicio. Es la única línea de la pantalla que trae trabajo
/// nuevo en vez de trabajo pendiente, así que va al final y desaparece cuando no hay ninguno.
class _RemindersCard extends ConsumerWidget {
  const _RemindersCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final recordatorios = ref.watch(remindersDueProvider).value ?? const [];
    if (recordatorios.isEmpty) return const SizedBox.shrink();

    final atrasados = recordatorios.where((r) => r.isOverdue).length;

    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.push('/recordatorios'),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
          child: Row(
            children: [
              Icon(Icons.directions_car_outlined,
                  color: theme.colorScheme.onSurfaceVariant),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '${recordatorios.length} '
                      '${recordatorios.length == 1 ? 'le toca' : 'les toca'} servicio',
                      style: theme.textTheme.titleSmall,
                    ),
                    Text(
                      atrasados == 0
                          ? 'este mes'
                          : 'este mes · $atrasados atrasado${atrasados == 1 ? '' : 's'}',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              Icon(Icons.chevron_right,
                  size: 18, color: theme.colorScheme.onSurfaceVariant),
            ],
          ),
        ),
      ),
    );
  }
}

TextStyle? _label(ThemeData theme) => theme.textTheme.labelSmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
      letterSpacing: 0.6,
    );
