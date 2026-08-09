import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/api/staff_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';

/// Bandeja de requerimientos del taller.
///
/// Es donde el Dueño decide qué entra a trabajarse y con quién: aprobar abre la orden y, en
/// el mismo gesto, la asigna a un técnico. Se hace en un solo paso a propósito —aprobar y
/// luego buscar la orden para asignarla son dos viajes por lo mismo, y el segundo se olvida.
///
/// El Técnico también entra, pero solo a mirar lo de sus sucursales: quién trabaja en qué es
/// una decisión del Dueño, y la API se lo impone aunque la pantalla se equivocara.
class ServiceRequestsScreen extends ConsumerStatefulWidget {
  const ServiceRequestsScreen({super.key});

  @override
  ConsumerState<ServiceRequestsScreen> createState() => _ServiceRequestsScreenState();
}

class _ServiceRequestsScreenState extends ConsumerState<ServiceRequestsScreen> {
  String? _busyId;

  bool get _isOwner {
    final auth = ref.read(authControllerProvider);
    return auth is AuthSignedIn && auth.user.role == AppRole.owner;
  }

  Future<void> _approve(ServiceRequestItem request) async {
    final technicians = (ref.read(technicianOptionsProvider).value ?? const [])
        .where((t) => t.worksAt(request.branchId))
        .toList();

    final choice = await showModalBottomSheet<_TechnicianChoice>(
      context: context,
      builder: (_) => _AssignSheet(request: request, technicians: technicians),
    );

    if (choice == null) return;

    setState(() => _busyId = request.id);
    try {
      final orderId = await ref.read(serviceRequestRepositoryProvider).approve(
            request.id,
            technicianId: choice.technicianId,
          );

      ref.invalidate(serviceRequestsProvider);
      ref.invalidate(myWorkOrdersProvider);

      if (!mounted) return;
      // Aprobar lleva a la orden recién creada: es el siguiente paso natural, y de paso
      // confirma que se creó sin tener que buscarla.
      context.push('/ordenes/$orderId');
    } catch (e) {
      _snack(apiErrorMessage(e, 'No se pudo aprobar el requerimiento.'));
    } finally {
      if (mounted) setState(() => _busyId = null);
    }
  }

  Future<void> _reject(ServiceRequestItem request) async {
    final controller = TextEditingController();

    final reason = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Rechazar el requerimiento'),
        content: TextField(
          controller: controller,
          autofocus: true,
          textCapitalization: TextCapitalization.sentences,
          decoration: const InputDecoration(
            labelText: 'Motivo',
            helperText: 'Queda registrado y el cliente lo ve.',
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(
            onPressed: () => Navigator.pop(context, controller.text.trim()),
            child: const Text('Rechazar'),
          ),
        ],
      ),
    );

    if (reason == null || reason.isEmpty) return;

    setState(() => _busyId = request.id);
    try {
      await ref.read(serviceRequestRepositoryProvider).reject(request.id, reason);
      ref.invalidate(serviceRequestsProvider);
    } catch (e) {
      _snack(apiErrorMessage(e, 'No se pudo rechazar el requerimiento.'));
    } finally {
      if (mounted) setState(() => _busyId = null);
    }
  }

  void _snack(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    final requests = ref.watch(serviceRequestsProvider);
    // Se pide aquí para que la lista ya esté en memoria cuando se toque "Aprobar".
    ref.watch(technicianOptionsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Requerimientos')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.push('/nueva-cita'),
        icon: const Icon(Icons.add),
        label: const Text('Recibir vehículo'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(serviceRequestsProvider),
        child: requests.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(
            children: [
              const SizedBox(height: 120),
              Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text(
                    apiErrorMessage(e, 'No se pudieron cargar los requerimientos.'),
                    textAlign: TextAlign.center,
                  ),
                ),
              ),
            ],
          ),
          data: (items) => items.isEmpty
              ? ListView(
                  children: const [
                    SizedBox(height: 120),
                    Center(
                      child: Padding(
                        padding: EdgeInsets.all(24),
                        child: Text(
                          'No hay requerimientos por atender.',
                          textAlign: TextAlign.center,
                        ),
                      ),
                    ),
                  ],
                )
              : ListView.builder(
                  padding: const EdgeInsets.all(12),
                  itemCount: items.length,
                  itemBuilder: (_, i) => _RequestCard(
                    request: items[i],
                    canDecide: _isOwner,
                    busy: _busyId == items[i].id,
                    onApprove: () => _approve(items[i]),
                    onReject: () => _reject(items[i]),
                  ),
                ),
        ),
      ),
    );
  }
}

class _RequestCard extends StatelessWidget {
  const _RequestCard({
    required this.request,
    required this.canDecide,
    required this.busy,
    required this.onApprove,
    required this.onReject,
  });

  final ServiceRequestItem request;
  final bool canDecide;
  final bool busy;
  final VoidCallback onApprove;
  final VoidCallback onReject;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(request.vehicleLabel, style: theme.textTheme.titleSmall),
                ),
                Chip(
                  label: Text(request.status.label, style: theme.textTheme.labelSmall),
                  visualDensity: VisualDensity.compact,
                  padding: EdgeInsets.zero,
                ),
              ],
            ),
            const SizedBox(height: 2),
            Text(
              '${request.customerName} · ${request.branchName} · ${_ago(request.createdAt)}',
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 8),
            Text(request.description, style: theme.textTheme.bodyMedium),

            if (request.reportedSymptoms != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  'Síntomas: ${request.reportedSymptoms}',
                  style: theme.textTheme.bodySmall,
                ),
              ),

            if (request.mileage != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text('${request.mileage} km', style: theme.textTheme.bodySmall),
              ),

            if (request.rejectionReason != null)
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text(
                  'Rechazado: ${request.rejectionReason}',
                  style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
                ),
              ),

            if (request.isPending && canDecide)
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: Row(
                  children: [
                    Expanded(
                      child: FilledButton(
                        onPressed: busy ? null : onApprove,
                        child: const Text('Aprobar y asignar'),
                      ),
                    ),
                    const SizedBox(width: 8),
                    TextButton(
                      onPressed: busy ? null : onReject,
                      child: const Text('Rechazar'),
                    ),
                  ],
                ),
              )
            else if (request.workOrderId != null)
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton(
                  onPressed: () => context.push('/ordenes/${request.workOrderId}'),
                  child: Text('Ver orden ${request.workOrderNumber ?? ''}'.trim()),
                ),
              ),
          ],
        ),
      ),
    );
  }

  static String _ago(DateTime value) {
    final diff = DateTime.now().difference(value.toLocal());

    if (diff.inMinutes < 60) return 'hace ${diff.inMinutes} min';
    if (diff.inHours < 24) return 'hace ${diff.inHours} h';
    return 'hace ${diff.inDays} d';
  }
}

class _TechnicianChoice {
  const _TechnicianChoice(this.technicianId);

  /// Null es «asignar después»: la orden se abre sin dueño y aparece sin asignar.
  final String? technicianId;
}

class _AssignSheet extends StatelessWidget {
  const _AssignSheet({required this.request, required this.technicians});

  final ServiceRequestItem request;
  final List<TechnicianOption> technicians;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('¿Quién lo atiende?', style: theme.textTheme.titleMedium),
            Text(
              '${request.vehicleLabel} · ${request.branchName}',
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 12),

            // Solo los de esa sucursal: la API rechaza asignar a uno de otra, y ofrecerlo
            // sería enseñar un botón que solo puede dar error.
            if (technicians.isEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Text(
                  'No hay técnicos asignados a esta sucursal. Se puede abrir la orden y '
                  'asignarla después desde el detalle.',
                  style: theme.textTheme.bodySmall,
                ),
              ),

            for (final technician in technicians)
              ListTile(
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.person_outline),
                title: Text(technician.name),
                onTap: () => Navigator.pop(context, _TechnicianChoice(technician.id)),
              ),

            const Divider(),
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.schedule_outlined),
              title: const Text('Asignar después'),
              subtitle: const Text('Abre la orden sin técnico'),
              onTap: () => Navigator.pop(context, const _TechnicianChoice(null)),
            ),
            const SizedBox(height: 4),
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(
                onPressed: () => Navigator.pop(context),
                child: const Text('Cancelar'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
