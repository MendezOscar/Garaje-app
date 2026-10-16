import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/api/appointment_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/garaj_skeleton.dart';
import '../../core/widgets/garaj_states.dart';
import '../shared/selector_de_sucursal.dart';

/// La agenda del taller: los días que los clientes apartaron.
///
/// Una cita todavía no es nada —ni orden, ni requerimiento, ni cliente del padrón—, así que
/// esta pantalla solo hace tres cosas: confirmar, rechazar y, cuando el carro llega, cerrarla
/// para pasar a recibir el vehículo, que es donde se toman los datos de verdad.
class AppointmentsScreen extends ConsumerStatefulWidget {
  const AppointmentsScreen({super.key});

  @override
  ConsumerState<AppointmentsScreen> createState() => _AppointmentsScreenState();
}

enum _Filtro { porConfirmar, hoy, todas }

class _AppointmentsScreenState extends ConsumerState<AppointmentsScreen> {
  _Filtro _filtro = _Filtro.porConfirmar;
  String? _busyId;

  List<AppointmentItem> _visibles(List<AppointmentItem> citas) => switch (_filtro) {
        _Filtro.porConfirmar => citas.where((c) => c.esperaRespuesta).toList(),
        _Filtro.hoy => citas.where((c) => c.esDeHoy).toList(),
        _Filtro.todas => citas,
      };

  Future<void> _confirmar(AppointmentItem cita) async {
    final cambio = await showModalBottomSheet<({DateTime dia, AppointmentSlot franja, String nota})>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _ConfirmSheet(cita: cita),
    );

    if (cambio == null) return;

    setState(() => _busyId = cita.id);
    try {
      await ref.read(appointmentRepositoryProvider).confirm(
            cita.id,
            day: cambio.dia == cita.day ? null : cambio.dia,
            slot: cambio.franja == cita.slot ? null : cambio.franja,
            note: cambio.nota.isEmpty ? null : cambio.nota,
          );

      ref.invalidate(appointmentsProvider);
      _snack('Confirmada. Avísele por WhatsApp a ${cita.contactName}.');
    } catch (e) {
      _snack(apiErrorMessage(e, 'No se pudo confirmar la cita.'));
    } finally {
      if (mounted) setState(() => _busyId = null);
    }
  }

  Future<void> _rechazar(AppointmentItem cita) async {
    final controller = TextEditingController();

    final motivo = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Rechazar la cita'),
        content: TextField(
          controller: controller,
          autofocus: true,
          textCapitalization: TextCapitalization.sentences,
          decoration: const InputDecoration(
            labelText: 'Motivo',
            helperText: 'Es lo que el cliente va a leer.',
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

    if (motivo == null || motivo.isEmpty) return;

    setState(() => _busyId = cita.id);
    try {
      await ref.read(appointmentRepositoryProvider).reject(cita.id, motivo);
      ref.invalidate(appointmentsProvider);
    } catch (e) {
      _snack(apiErrorMessage(e, 'No se pudo rechazar la cita.'));
    } finally {
      if (mounted) setState(() => _busyId = null);
    }
  }

  /// Llegó el vehículo: se cierra la cita y se pasa a recibirlo, que es donde se toman la
  /// placa, la marca y el año de verdad.
  Future<void> _llego(AppointmentItem cita) async {
    setState(() => _busyId = cita.id);
    try {
      await ref.read(appointmentRepositoryProvider).attended(cita.id);
      ref.invalidate(appointmentsProvider);

      if (!mounted) return;
      context.push('/nueva-cita');
    } catch (e) {
      _snack(apiErrorMessage(e, 'No se pudo cerrar la cita.'));
    } finally {
      if (mounted) setState(() => _busyId = null);
    }
  }

  Future<void> _whatsapp(AppointmentItem cita) async {
    final numero = cita.contactPhone.replaceAll(RegExp(r'[^0-9]'), '');
    // Honduras es +504 y los teléfonos se guardan sin código: wa.me lo exige completo.
    final completo = numero.length == 8 ? '504$numero' : numero;

    final cuando = '${_fecha(cita.day)} ${cita.slot.label.toLowerCase()}';
    final texto = cita.status == AppointmentStatus.confirmed
        ? 'Hola ${cita.contactName}, su cita quedó confirmada para el $cuando. Lo esperamos.'
        : 'Hola ${cita.contactName}, recibimos su solicitud de cita para el $cuando.';

    await launchUrl(
      Uri.parse('https://wa.me/$completo?text=${Uri.encodeComponent(texto)}'),
      mode: LaunchMode.externalApplication,
    );
  }

  void _snack(String mensaje) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(mensaje)));
  }

  @override
  Widget build(BuildContext context) {
    final citas = ref.watch(appointmentsProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Citas'),
        actions: const [SelectorDeSucursal(), SizedBox(width: 8)],
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () async {
          final hecha = await showModalBottomSheet<bool>(
            context: context,
            isScrollControlled: true,
            builder: (_) => const _NuevaCitaSheet(),
          );
          if (hecha ?? false) ref.invalidate(appointmentsProvider);
        },
        icon: const Icon(Icons.event_available),
        label: const Text('Anotar cita'),
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
            child: Row(
              children: [
                for (final filtro in _Filtro.values)
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: ChoiceChip(
                      label: Text(switch (filtro) {
                        _Filtro.porConfirmar => 'Por confirmar',
                        _Filtro.hoy => 'Hoy',
                        _Filtro.todas => 'Todas',
                      }),
                      selected: filtro == _filtro,
                      onSelected: (_) => setState(() => _filtro = filtro),
                    ),
                  ),
              ],
            ),
          ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async => ref.invalidate(appointmentsProvider),
              child: citas.when(
                loading: () => const GarajSkeletonList(),
                error: (e, _) => GarajError(
                  message: apiErrorMessage(e, 'No se pudieron cargar las citas.'),
                  onRetry: () => ref.invalidate(appointmentsProvider),
                ),
                data: (todas) {
                  final lista = _visibles(todas);

                  if (lista.isEmpty) {
                    return GarajEmpty(
                      title: _filtro == _Filtro.porConfirmar
                          ? 'Ninguna cita espera respuesta'
                          : 'No hay citas',
                      hint: _filtro == _Filtro.porConfirmar
                          ? 'Al día.'
                          : 'Las que pidan por el enlace o desde la app aparecen aquí.',
                    );
                  }

                  return ListView.builder(
                    padding: const EdgeInsets.all(12),
                    itemCount: lista.length,
                    itemBuilder: (_, i) => _CitaCard(
                      cita: lista[i],
                      busy: _busyId == lista[i].id,
                      onConfirmar: () => _confirmar(lista[i]),
                      onRechazar: () => _rechazar(lista[i]),
                      onLlego: () => _llego(lista[i]),
                      onWhatsapp: () => _whatsapp(lista[i]),
                    ),
                  );
                },
              ),
            ),
          ),
        ],
      ),
    );
  }
}

String _fecha(DateTime dia) =>
    '${dia.day.toString().padLeft(2, '0')}/${dia.month.toString().padLeft(2, '0')}/${dia.year}';

class _CitaCard extends StatelessWidget {
  const _CitaCard({
    required this.cita,
    required this.busy,
    required this.onConfirmar,
    required this.onRechazar,
    required this.onLlego,
    required this.onWhatsapp,
  });

  final AppointmentItem cita;
  final bool busy;
  final VoidCallback onConfirmar;
  final VoidCallback onRechazar;
  final VoidCallback onLlego;
  final VoidCallback onWhatsapp;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    '${_fecha(cita.day)} · ${cita.slot.label.toLowerCase()}',
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                Chip(
                  label: Text(cita.status.label),
                  visualDensity: VisualDensity.compact,
                  padding: EdgeInsets.zero,
                ),
              ],
            ),
            const SizedBox(height: 4),
            Text(cita.contactName, style: theme.textTheme.titleSmall),
            Text(
              '${cita.vehicleLabel} · ${cita.branchName} · ${cita.source.label}',
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 6),
            Text(cita.reason),
            if (cita.responseNote != null) ...[
              const SizedBox(height: 4),
              Text('↳ ${cita.responseNote}', style: theme.textTheme.bodySmall),
            ],
            const SizedBox(height: 8),
            Row(
              children: [
                IconButton(
                  onPressed: busy ? null : onWhatsapp,
                  icon: const Icon(Icons.chat),
                  tooltip: 'Escribirle por WhatsApp',
                ),
                const Spacer(),
                if (cita.status == AppointmentStatus.requested) ...[
                  TextButton(onPressed: busy ? null : onRechazar, child: const Text('Rechazar')),
                  const SizedBox(width: 8),
                  FilledButton(
                    onPressed: busy ? null : onConfirmar,
                    child: const Text('Confirmar'),
                  ),
                ] else if (cita.status == AppointmentStatus.confirmed) ...[
                  TextButton(
                    onPressed: busy ? null : onConfirmar,
                    child: const Text('Cambiar día'),
                  ),
                  const SizedBox(width: 8),
                  FilledButton(onPressed: busy ? null : onLlego, child: const Text('Llegó')),
                ],
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Confirmar, pudiendo mover el día: «ese día no, pero el jueves sí».
class _ConfirmSheet extends StatefulWidget {
  const _ConfirmSheet({required this.cita});

  final AppointmentItem cita;

  @override
  State<_ConfirmSheet> createState() => _ConfirmSheetState();
}

class _ConfirmSheetState extends State<_ConfirmSheet> {
  late DateTime _dia = widget.cita.day;
  late AppointmentSlot _franja = widget.cita.slot;
  final _nota = TextEditingController();

  @override
  void dispose() {
    _nota.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(context).viewInsets.bottom + 16),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Confirmar la cita', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 4),
          Text(
            '${widget.cita.contactName} · ${widget.cita.vehicleLabel}',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 16),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.calendar_today),
            title: Text(_fecha(_dia)),
            trailing: const Text('Cambiar'),
            onTap: () async {
              final hoy = DateTime.now();
              final elegido = await showDatePicker(
                context: context,
                initialDate: _dia.isBefore(hoy) ? hoy : _dia,
                firstDate: DateTime(hoy.year, hoy.month, hoy.day),
                lastDate: hoy.add(const Duration(days: 60)),
              );
              if (elegido != null) setState(() => _dia = elegido);
            },
          ),
          SegmentedButton<AppointmentSlot>(
            segments: const [
              ButtonSegment(value: AppointmentSlot.morning, label: Text('Mañana')),
              ButtonSegment(value: AppointmentSlot.afternoon, label: Text('Tarde')),
            ],
            selected: {_franja},
            onSelectionChanged: (s) => setState(() => _franja = s.first),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _nota,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              labelText: 'Nota para el cliente',
              helperText: 'Opcional. La ve al abrir su enlace.',
            ),
          ),
          const SizedBox(height: 16),
          SizedBox(
            width: double.infinity,
            child: FilledButton(
              onPressed: () => Navigator.pop(
                context,
                (dia: _dia, franja: _franja, nota: _nota.text.trim()),
              ),
              child: const Text('Confirmar'),
            ),
          ),
        ],
      ),
    );
  }
}

/// La cita que se acuerda por teléfono o en el mostrador. Nace confirmada.
class _NuevaCitaSheet extends ConsumerStatefulWidget {
  const _NuevaCitaSheet();

  @override
  ConsumerState<_NuevaCitaSheet> createState() => _NuevaCitaSheetState();
}

class _NuevaCitaSheetState extends ConsumerState<_NuevaCitaSheet> {
  final _nombre = TextEditingController();
  final _telefono = TextEditingController();
  final _vehiculo = TextEditingController();
  final _motivo = TextEditingController();

  DateTime _dia = DateTime.now();
  AppointmentSlot _franja = AppointmentSlot.morning;
  String? _sucursalId;
  bool _guardando = false;

  @override
  void dispose() {
    _nombre.dispose();
    _telefono.dispose();
    _vehiculo.dispose();
    _motivo.dispose();
    super.dispose();
  }

  Future<void> _guardar(List<({String id, String name})> sucursales) async {
    if (_nombre.text.trim().isEmpty ||
        _telefono.text.trim().isEmpty ||
        _motivo.text.trim().isEmpty) {
      return;
    }

    setState(() => _guardando = true);
    try {
      await ref.read(appointmentRepositoryProvider).create(
            branchId: _sucursalId ?? sucursales.first.id,
            contactName: _nombre.text.trim(),
            contactPhone: _telefono.text.trim(),
            vehicleNote: _vehiculo.text.trim().isEmpty ? null : _vehiculo.text.trim(),
            reason: _motivo.text.trim(),
            day: _dia,
            slot: _franja,
          );

      if (!mounted) return;
      Navigator.pop(context, true);
    } catch (e) {
      if (!mounted) return;
      setState(() => _guardando = false);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e, 'No se pudo anotar la cita.'))),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = ref.watch(authControllerProvider);
    final sucursales = auth is AuthSignedIn
        ? auth.user.branches.map((b) => (id: b.id, name: b.name)).toList()
        : <({String id, String name})>[];

    return Padding(
      padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(context).viewInsets.bottom + 16),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Anotar una cita', style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 4),
            Text(
              'La que se acuerda por teléfono. Nace confirmada.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _nombre,
              textCapitalization: TextCapitalization.words,
              decoration: const InputDecoration(labelText: 'Nombre'),
            ),
            TextField(
              controller: _telefono,
              keyboardType: TextInputType.phone,
              decoration: const InputDecoration(labelText: 'Teléfono'),
            ),
            TextField(
              controller: _vehiculo,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Qué vehículo',
                hintText: 'Corolla 2014 gris',
              ),
            ),
            TextField(
              controller: _motivo,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Qué necesita'),
            ),
            const SizedBox(height: 8),
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.calendar_today),
              title: Text(_fecha(_dia)),
              trailing: const Text('Cambiar'),
              onTap: () async {
                final hoy = DateTime.now();
                final elegido = await showDatePicker(
                  context: context,
                  initialDate: _dia,
                  firstDate: DateTime(hoy.year, hoy.month, hoy.day),
                  lastDate: hoy.add(const Duration(days: 60)),
                );
                if (elegido != null) setState(() => _dia = elegido);
              },
            ),
            SegmentedButton<AppointmentSlot>(
              segments: const [
                ButtonSegment(value: AppointmentSlot.morning, label: Text('Mañana')),
                ButtonSegment(value: AppointmentSlot.afternoon, label: Text('Tarde')),
              ],
              selected: {_franja},
              onSelectionChanged: (s) => setState(() => _franja = s.first),
            ),
            if (sucursales.length > 1) ...[
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                initialValue: _sucursalId ?? sucursales.first.id,
                decoration: const InputDecoration(labelText: 'Sucursal'),
                items: [
                  for (final s in sucursales)
                    DropdownMenuItem(value: s.id, child: Text(s.name)),
                ],
                onChanged: (v) => setState(() => _sucursalId = v),
              ),
            ],
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: _guardando || sucursales.isEmpty ? null : () => _guardar(sucursales),
                child: Text(_guardando ? 'Guardando…' : 'Anotar'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
