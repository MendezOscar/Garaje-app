import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_image_compress/flutter_image_compress.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';
import 'package:path_provider/path_provider.dart';

import '../../core/api/api_client.dart';
import '../../core/api/media_repository.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/api/work_order_repository.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';
import '../../core/models/media.dart';
import '../../core/models/work_order.dart';

/// Alta de un requerimiento. La misma pantalla para el cliente que pide cita desde su casa y
/// para el taller que recibe la moto en el mostrador.
///
/// Que el taller pueda registrarlo no es un extra: casi nadie llega con la aplicación
/// instalada, y menos al principio. Si el requerimiento solo pudiera entrar desde el teléfono
/// del cliente, la bandeja estaría siempre vacía y el trabajo real seguiría anotándose en un
/// cuaderno. Por eso el mostrador puede además dar de alta cliente y vehículo sobre la marcha.
///
/// Las fotos se toman antes de que el requerimiento exista, así que se guardan en memoria y
/// se suben después de crearlo: la API necesita un dueño al que colgarlas. Si alguna falla,
/// el requerimiento igual queda creado —perder la cita por una foto sería absurdo.
class NewServiceRequestScreen extends ConsumerStatefulWidget {
  const NewServiceRequestScreen({super.key});

  @override
  ConsumerState<NewServiceRequestScreen> createState() => _NewServiceRequestScreenState();
}

class _NewServiceRequestScreenState extends ConsumerState<NewServiceRequestScreen> {
  final _formKey = GlobalKey<FormState>();
  final _description = TextEditingController();
  final _symptoms = TextEditingController();
  final _mileage = TextEditingController();

  String? _vehicleId;
  String? _branchId;
  DateTime? _preferredDate;
  final List<File> _photos = [];

  /// Búsqueda de vehículo del mostrador. Vacía para el Cliente: sus vehículos son pocos y
  /// ya vienen filtrados por la API.
  String _search = '';

  bool _saving = false;
  String? _error;

  bool get _isStaff {
    final auth = ref.read(authControllerProvider);
    return auth is AuthSignedIn && auth.user.role != AppRole.customer;
  }

  @override
  void dispose() {
    _description.dispose();
    _symptoms.dispose();
    _mileage.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final staff = _isStaff;
    final vehicles = ref.watch(vehicleOptionsProvider(_search));
    final branches = ref.watch(branchOptionsProvider);

    return Scaffold(
      appBar: AppBar(title: Text(staff ? 'Recibir vehículo' : 'Pedir cita')),
      body: vehicles.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(child: Text(apiErrorMessage(e, 'No se pudo cargar la información.'))),
        data: (vehicleList) {
          if (vehicleList.isEmpty && !staff) {
            return const Center(
              child: Padding(
                padding: EdgeInsets.all(32),
                child: Text(
                  'Todavía no tiene vehículos registrados. Pídale al taller que agregue el suyo.',
                  textAlign: TextAlign.center,
                ),
              ),
            );
          }

          // Si el vehículo elegido ya no está en la lista —porque cambió la búsqueda— se
          // vuelve al primero: dejarlo apuntando a uno que no se ve haría enviar el
          // requerimiento a un vehículo que el usuario no tiene delante.
          if (!vehicleList.any((v) => v.id == _vehicleId)) {
            _vehicleId = vehicleList.firstOrNull?.id;
          }
          _branchId ??= branches.value?.firstOrNull?.id;

          return Form(
            key: _formKey,
            child: ListView(
              padding: const EdgeInsets.all(16),
              children: [
                if (staff) ...[
                  TextFormField(
                    initialValue: _search,
                    decoration: const InputDecoration(
                      labelText: 'Buscar vehículo',
                      hintText: 'Placa, marca o nombre del dueño',
                      prefixIcon: Icon(Icons.search),
                    ),
                    textInputAction: TextInputAction.search,
                    onFieldSubmitted: (value) => setState(() => _search = value),
                  ),
                  const SizedBox(height: 8),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton.icon(
                      onPressed: _registerNewCustomer,
                      icon: const Icon(Icons.person_add_alt, size: 18),
                      label: const Text('Cliente nuevo'),
                    ),
                  ),
                  if (vehicleList.isEmpty)
                    const Padding(
                      padding: EdgeInsets.symmetric(vertical: 8),
                      child: Text('Ningún vehículo coincide con la búsqueda.'),
                    ),
                  const SizedBox(height: 4),
                ],

                if (vehicleList.isNotEmpty)
                  DropdownButtonFormField<String>(
                    initialValue: _vehicleId,
                    isExpanded: true,
                    decoration: const InputDecoration(labelText: 'Vehículo'),
                    items: [
                      for (final vehicle in vehicleList)
                        DropdownMenuItem(
                          value: vehicle.id,
                          child: Text(
                            staff && vehicle.customerName.isNotEmpty
                                ? '${vehicle.label} · ${vehicle.customerName}'
                                : vehicle.label,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                    ],
                    onChanged: (value) => setState(() => _vehicleId = value),
                  ),
                const SizedBox(height: 12),

                DropdownButtonFormField<String>(
                  initialValue: _branchId,
                  decoration: const InputDecoration(labelText: 'Sucursal'),
                  items: [
                    for (final branch in branches.value ?? <BranchOption>[])
                      DropdownMenuItem(value: branch.id, child: Text(branch.name)),
                  ],
                  onChanged: (value) => setState(() => _branchId = value),
                ),
                const SizedBox(height: 12),

                TextFormField(
                  controller: _description,
                  decoration: InputDecoration(
                    labelText: staff ? 'Motivo de ingreso' : '¿Qué necesita?',
                    hintText: 'Cambio de aceite, revisión de frenos…',
                  ),
                  maxLines: 2,
                  textCapitalization: TextCapitalization.sentences,
                  validator: (value) => value == null || value.trim().isEmpty
                      ? (staff ? 'Escriba por qué entra el vehículo.' : 'Cuéntenos qué necesita.')
                      : null,
                ),
                const SizedBox(height: 12),

                TextFormField(
                  controller: _symptoms,
                  decoration: InputDecoration(
                    labelText: staff ? '¿Qué reporta el cliente?' : '¿Qué ha notado?',
                    hintText: 'Un ruido al frenar, se calienta en el tráfico… (opcional)',
                  ),
                  maxLines: 2,
                  textCapitalization: TextCapitalization.sentences,
                ),
                const SizedBox(height: 12),

                TextFormField(
                  controller: _mileage,
                  decoration: const InputDecoration(labelText: 'Kilometraje (opcional)'),
                  keyboardType: TextInputType.number,
                ),
                const SizedBox(height: 12),

                // Solo tiene sentido cuando se pide una cita a futuro. El vehículo que ya
                // está en el mostrador entra hoy.
                if (!staff)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    leading: const Icon(Icons.event_outlined),
                    title: Text(_preferredDate == null
                        ? 'Fecha preferida (opcional)'
                        : '${_preferredDate!.day}/${_preferredDate!.month}/${_preferredDate!.year}'),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: _pickDate,
                  ),

                const Divider(height: 24),
                const Text('FOTOS', style: TextStyle(fontSize: 12, letterSpacing: 1)),
                const SizedBox(height: 8),

                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final photo in _photos)
                      Stack(
                        children: [
                          ClipRRect(
                            borderRadius: BorderRadius.circular(6),
                            child: Image.file(photo, width: 84, height: 84, fit: BoxFit.cover),
                          ),
                          Positioned(
                            top: -6,
                            right: -6,
                            child: IconButton(
                              icon: const Icon(Icons.cancel, size: 20),
                              onPressed: () => setState(() => _photos.remove(photo)),
                            ),
                          ),
                        ],
                      ),
                    IconButton.filledTonal(
                      tooltip: 'Tomar foto',
                      icon: const Icon(Icons.photo_camera_outlined),
                      onPressed: () => _addPhoto(ImageSource.camera),
                    ),
                    IconButton.filledTonal(
                      tooltip: 'Elegir de la galería',
                      icon: const Icon(Icons.photo_library_outlined),
                      onPressed: () => _addPhoto(ImageSource.gallery),
                    ),
                  ],
                ),

                if (_error != null) ...[
                  const SizedBox(height: 16),
                  Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
                ],

                const SizedBox(height: 24),
                FilledButton(
                  onPressed: _saving ? null : _submit,
                  child: Text(
                    _saving
                        ? 'Guardando…'
                        : staff
                            ? 'Registrar requerimiento'
                            : 'Enviar al taller',
                  ),
                ),
              ],
            ),
          );
        },
      ),
    );
  }

  /// Alta exprés desde el mostrador: nombre, teléfono y los datos de la moto, nada más.
  /// El resto de la ficha —correo, dirección, RTN— lo completa el Dueño después desde el
  /// panel; pedirlo aquí, con el cliente esperando de pie, garantiza que no se registre.
  Future<void> _registerNewCustomer() async {
    final created = await showModalBottomSheet<VehicleOption>(
      context: context,
      isScrollControlled: true,
      builder: (_) => const _NewCustomerSheet(),
    );

    if (created == null || !mounted) return;

    setState(() {
      // Se busca por la placa para que la lista quede mostrando justo lo recién creado.
      _search = created.searchTerm;
      _vehicleId = created.id;
    });
    ref.invalidate(vehicleOptionsProvider(created.searchTerm));
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();

    final picked = await showDatePicker(
      context: context,
      initialDate: _preferredDate ?? now.add(const Duration(days: 1)),
      firstDate: now,
      lastDate: now.add(const Duration(days: 90)),
    );

    if (picked != null) setState(() => _preferredDate = picked);
  }

  Future<void> _addPhoto(ImageSource source) async {
    final picked = await ImagePicker().pickImage(
      source: source,
      maxWidth: 1600,
      maxHeight: 1600,
      imageQuality: 85,
    );

    if (picked == null) return;

    final compressed = await _compress(File(picked.path));
    if (mounted) setState(() => _photos.add(compressed));
  }

  Future<File> _compress(File source) async {
    final directory = await getTemporaryDirectory();
    final target = '${directory.path}/req_${source.uri.pathSegments.last}';

    final result = await FlutterImageCompress.compressAndGetFile(
      source.absolute.path,
      target,
      quality: 80,
      minWidth: 1280,
      minHeight: 1280,
    );

    return result == null ? source : File(result.path);
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    if (_vehicleId == null || _branchId == null) {
      setState(() => _error = 'Elija el vehículo y la sucursal.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });

    try {
      final id = await ref.read(serviceRequestRepositoryProvider).create(
            branchId: _branchId!,
            vehicleId: _vehicleId!,
            description: _description.text.trim(),
            reportedSymptoms: _symptoms.text.trim().isEmpty ? null : _symptoms.text.trim(),
            preferredDate: _preferredDate,
            mileage: int.tryParse(_mileage.text.trim()),
          );

      var failed = 0;
      for (final photo in _photos) {
        try {
          await ref.read(mediaRepositoryProvider).upload(
                file: photo,
                ownerType: MediaOwnerType.serviceRequest,
                ownerId: id,
                takenAt: DateTime.now(),
              );
        } catch (_) {
          failed++;
        }
      }

      if (!mounted) return;

      final done = _isStaff ? 'Requerimiento registrado.' : 'Su solicitud llegó al taller.';

      ref.invalidate(myWorkOrdersProvider);
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(failed == 0
            ? done
            : '$done $failed foto(s) no se pudieron subir.'),
      ));
      context.pop();
    } catch (e) {
      setState(() => _error = apiErrorMessage(e, 'No se pudo enviar la solicitud.'));
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }
}

/// Alta de cliente y vehículo en un solo paso, desde el mostrador.
///
/// Devuelve el vehículo creado, que es lo que la pantalla de atrás necesita para seguir.
/// Pide lo mínimo: el teléfono es obligatorio porque es por donde se manda la cotización
/// por WhatsApp, y sin él el resto del flujo se rompe más adelante.
class _NewCustomerSheet extends ConsumerStatefulWidget {
  const _NewCustomerSheet();

  @override
  ConsumerState<_NewCustomerSheet> createState() => _NewCustomerSheetState();
}

class _NewCustomerSheetState extends ConsumerState<_NewCustomerSheet> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _phone = TextEditingController();
  final _brand = TextEditingController();
  final _model = TextEditingController();
  final _plate = TextEditingController();

  VehicleType _type = VehicleType.motorcycle;
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _phone.dispose();
    _brand.dispose();
    _model.dispose();
    _plate.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    setState(() {
      _saving = true;
      _error = null;
    });

    try {
      final vehicle = await ref.read(serviceRequestRepositoryProvider).registerCustomerAndVehicle(
            fullName: _name.text.trim(),
            phone: _phone.text.trim(),
            vehicleType: _type.value,
            brand: _brand.text.trim(),
            model: _model.text.trim(),
            plate: _plate.text.trim().isEmpty ? null : _plate.text.trim(),
          );

      if (mounted) Navigator.pop(context, vehicle);
    } catch (e) {
      setState(() => _error = apiErrorMessage(e, 'No se pudo registrar el cliente.'));
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      // Deja sitio al teclado: sin esto el último campo queda debajo y no se puede escribir.
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        top: 16,
        bottom: MediaQuery.of(context).viewInsets.bottom + 16,
      ),
      child: Form(
        key: _formKey,
        child: ListView(
          shrinkWrap: true,
          children: [
            Text('Cliente nuevo', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 12),

            TextFormField(
              controller: _name,
              decoration: const InputDecoration(labelText: 'Nombre completo'),
              textCapitalization: TextCapitalization.words,
              validator: (v) => v == null || v.trim().isEmpty ? 'Falta el nombre.' : null,
            ),
            const SizedBox(height: 12),

            TextFormField(
              controller: _phone,
              decoration: const InputDecoration(
                labelText: 'Teléfono',
                hintText: '50499998888',
              ),
              keyboardType: TextInputType.phone,
              validator: (v) => v == null || v.trim().isEmpty ? 'Falta el teléfono.' : null,
            ),
            const SizedBox(height: 20),

            SegmentedButton<VehicleType>(
              segments: const [
                ButtonSegment(value: VehicleType.motorcycle, label: Text('Moto')),
                ButtonSegment(value: VehicleType.car, label: Text('Vehículo')),
              ],
              selected: {_type},
              onSelectionChanged: (s) => setState(() => _type = s.first),
            ),
            const SizedBox(height: 12),

            Row(
              children: [
                Expanded(
                  child: TextFormField(
                    controller: _brand,
                    decoration: const InputDecoration(labelText: 'Marca'),
                    textCapitalization: TextCapitalization.words,
                    validator: (v) => v == null || v.trim().isEmpty ? 'Falta la marca.' : null,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextFormField(
                    controller: _model,
                    decoration: const InputDecoration(labelText: 'Modelo'),
                    validator: (v) => v == null || v.trim().isEmpty ? 'Falta el modelo.' : null,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),

            TextFormField(
              controller: _plate,
              decoration: const InputDecoration(
                labelText: 'Placa (opcional)',
                helperText: 'Sin guiones ni espacios.',
              ),
              textCapitalization: TextCapitalization.characters,
            ),

            if (_error != null) ...[
              const SizedBox(height: 16),
              Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],

            const SizedBox(height: 20),
            FilledButton(
              onPressed: _saving ? null : _save,
              child: Text(_saving ? 'Guardando…' : 'Registrar'),
            ),
          ],
        ),
      ),
    );
  }
}
