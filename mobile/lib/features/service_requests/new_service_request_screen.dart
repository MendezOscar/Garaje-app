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
import '../../core/models/media.dart';

/// El cliente pide una cita desde el teléfono, con fotos de lo que le preocupa.
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

  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _description.dispose();
    _symptoms.dispose();
    _mileage.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final vehicles = ref.watch(vehicleOptionsProvider);
    final branches = ref.watch(branchOptionsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Pedir cita')),
      body: vehicles.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(child: Text(apiErrorMessage(e, 'No se pudo cargar su información.'))),
        data: (vehicleList) {
          if (vehicleList.isEmpty) {
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

          _vehicleId ??= vehicleList.first.id;
          _branchId ??= branches.value?.firstOrNull?.id;

          return Form(
            key: _formKey,
            child: ListView(
              padding: const EdgeInsets.all(16),
              children: [
                DropdownButtonFormField<String>(
                  initialValue: _vehicleId,
                  decoration: const InputDecoration(labelText: 'Vehículo'),
                  items: [
                    for (final vehicle in vehicleList)
                      DropdownMenuItem(value: vehicle.id, child: Text(vehicle.label)),
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
                  decoration: const InputDecoration(
                    labelText: '¿Qué necesita?',
                    hintText: 'Cambio de aceite, revisión de frenos…',
                  ),
                  maxLines: 2,
                  validator: (value) =>
                      value == null || value.trim().isEmpty ? 'Cuéntenos qué necesita.' : null,
                ),
                const SizedBox(height: 12),

                TextFormField(
                  controller: _symptoms,
                  decoration: const InputDecoration(
                    labelText: '¿Qué ha notado?',
                    hintText: 'Un ruido al frenar, se calienta en el tráfico… (opcional)',
                  ),
                  maxLines: 2,
                ),
                const SizedBox(height: 12),

                TextFormField(
                  controller: _mileage,
                  decoration: const InputDecoration(labelText: 'Kilometraje (opcional)'),
                  keyboardType: TextInputType.number,
                ),
                const SizedBox(height: 12),

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
                  child: Text(_saving ? 'Enviando…' : 'Enviar al taller'),
                ),
              ],
            ),
          );
        },
      ),
    );
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

      ref.invalidate(myWorkOrdersProvider);
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(failed == 0
            ? 'Su solicitud llegó al taller.'
            : 'Su solicitud llegó al taller. $failed foto(s) no se pudieron subir.'),
      ));
      context.pop();
    } catch (e) {
      setState(() => _error = apiErrorMessage(e, 'No se pudo enviar la solicitud.'));
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }
}
