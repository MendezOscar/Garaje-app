import 'dart:io';

import 'package:flutter_image_compress/flutter_image_compress.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';
import 'package:path_provider/path_provider.dart';

import '../../core/api/media_repository.dart';
import '../../core/models/media.dart';
import '../../core/sync/upload_queue.dart';

/// Toma la foto y la deja en la cola de subida. Devuelve false si no se tomó ninguna.
///
/// Fuera de la galería porque el técnico también dispara desde su pantalla de trabajo, sin
/// abrir la orden: documentar es lo que más se le olvida, y cada toque de más es una foto menos.
Future<bool> capturarFoto(
  WidgetRef ref, {
  required String workOrderId,
  ImageSource source = ImageSource.camera,
}) async {
  final picked = await ImagePicker().pickImage(
    source: source,
    // El teléfono da 12 MP; para ver un rayón o una pieza rota sobra mucho menos, y con
    // la señal del taller cada MB de más es medio minuto de espera.
    maxWidth: 1600,
    maxHeight: 1600,
    imageQuality: 85,
  );

  if (picked == null) return false;

  await ref.read(uploadQueueProvider.notifier).enqueue(
        photo: await comprimirFoto(File(picked.path)),
        ownerType: MediaOwnerType.workOrder,
        ownerId: workOrderId,
        takenAt: DateTime.now(),
      );

  ref.invalidate(workOrderMediaProvider(workOrderId));
  return true;
}

/// Recomprime a JPEG. `image_picker` ya redimensiona, pero en iOS entrega HEIC y el
/// backend solo genera miniatura de lo que sabe decodificar.
Future<File> comprimirFoto(File original) async {
  final directory = await getTemporaryDirectory();
  final target = '${directory.path}/${DateTime.now().microsecondsSinceEpoch}.jpg';

  final result = await FlutterImageCompress.compressAndGetFile(
    original.absolute.path,
    target,
    quality: 80,
    format: CompressFormat.jpeg,
  );

  return result == null ? original : File(result.path);
}
