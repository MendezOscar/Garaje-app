import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/appointment_repository.dart';
import '../../core/api/service_request_repository.dart';

/// Pedir una cita desde la app del cliente.
///
/// No describe el vehículo: lo elige entre los suyos, que el taller ya tiene registrados. Lo
/// único que se le pregunta es qué necesita y qué día puede traerlo; la hora exacta no se
/// promete, porque el taller no la puede cumplir.
class NewAppointmentScreen extends ConsumerStatefulWidget {
  const NewAppointmentScreen({super.key});

  @override
  ConsumerState<NewAppointmentScreen> createState() => _NewAppointmentScreenState();
}

class _NewAppointmentScreenState extends ConsumerState<NewAppointmentScreen> {
  final _motivo = TextEditingController();

  String? _vehiculoId;
  String? _sucursalId;
  DateTime _dia = DateTime.now().add(const Duration(days: 1));
  AppointmentSlot _franja = AppointmentSlot.morning;

  bool _guardando = false;
  String? _error;

  @override
  void dispose() {
    _motivo.dispose();
    super.dispose();
  }

  Future<void> _pedir(List<VehicleOption> vehiculos, List<BranchOption> sucursales) async {
    final vehiculoId = _vehiculoId ?? vehiculos.firstOrNull?.id;
    final sucursalId = _sucursalId ?? sucursales.firstOrNull?.id;

    if (vehiculoId == null || sucursalId == null || _motivo.text.trim().isEmpty) return;

    setState(() {
      _guardando = true;
      _error = null;
    });

    try {
      await ref.read(appointmentRepositoryProvider).request(
            branchId: sucursalId,
            vehicleId: vehiculoId,
            reason: _motivo.text.trim(),
            day: _dia,
            slot: _franja,
          );

      ref.invalidate(misCitasProvider);

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Pedida. El taller le confirma; va a recibir el aviso aquí.'),
        ),
      );
      Navigator.of(context).pop();
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _guardando = false;
        _error = apiErrorMessage(e, 'No se pudo pedir la cita.');
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final vehiculos = ref.watch(vehicleOptionsProvider(''));
    final sucursales = ref.watch(branchOptionsProvider);

    final misVehiculos = vehiculos.value ?? const <VehicleOption>[];
    final misSucursales = sucursales.value ?? const <BranchOption>[];
    final hoy = DateTime.now();

    return Scaffold(
      appBar: AppBar(title: const Text('Pedir una cita')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(
            'Aparte el día que va a traer su vehículo. El taller se lo confirma.',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          const SizedBox(height: 16),

          if (vehiculos.isLoading)
            const Center(child: Padding(padding: EdgeInsets.all(24), child: CircularProgressIndicator()))
          else if (misVehiculos.isEmpty)
            const Card(
              child: Padding(
                padding: EdgeInsets.all(16),
                child: Text(
                  'Su taller todavía no le ha registrado un vehículo. Escríbale para que se '
                  'lo registren al recibirlo.',
                ),
              ),
            )
          else ...[
            DropdownButtonFormField<String>(
              initialValue: _vehiculoId ?? misVehiculos.first.id,
              decoration: const InputDecoration(labelText: 'Cuál vehículo'),
              items: [
                for (final v in misVehiculos)
                  DropdownMenuItem(value: v.id, child: Text(v.label)),
              ],
              onChanged: (v) => setState(() => _vehiculoId = v),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _motivo,
              textCapitalization: TextCapitalization.sentences,
              maxLines: 3,
              decoration: const InputDecoration(
                labelText: 'Qué necesita',
                hintText: 'Cambio de aceite y revisar un ruido al frenar',
              ),
            ),
            const SizedBox(height: 12),
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.calendar_today),
              title: Text(
                '${_dia.day.toString().padLeft(2, '0')}/'
                '${_dia.month.toString().padLeft(2, '0')}/${_dia.year}',
              ),
              trailing: const Text('Cambiar'),
              onTap: () async {
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
                ButtonSegment(value: AppointmentSlot.morning, label: Text('Por la mañana')),
                ButtonSegment(value: AppointmentSlot.afternoon, label: Text('Por la tarde')),
              ],
              selected: {_franja},
              onSelectionChanged: (s) => setState(() => _franja = s.first),
            ),
            if (misSucursales.length > 1) ...[
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                initialValue: _sucursalId ?? misSucursales.first.id,
                decoration: const InputDecoration(labelText: 'A qué sucursal'),
                items: [
                  for (final s in misSucursales)
                    DropdownMenuItem(value: s.id, child: Text(s.name)),
                ],
                onChanged: (v) => setState(() => _sucursalId = v),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 12),
              Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
            const SizedBox(height: 20),
            FilledButton(
              onPressed: _guardando ? null : () => _pedir(misVehiculos, misSucursales),
              child: Text(_guardando ? 'Pidiendo…' : 'Pedir la cita'),
            ),
            const SizedBox(height: 10),
            Text(
              'Queda por confirmar hasta que el taller conteste. Si ese día no les cabe, le '
              'proponen otro.',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ],
      ),
    );
  }
}
