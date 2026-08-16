import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:path_provider/path_provider.dart';

import '../api/media_repository.dart';
import '../models/media.dart';

/// Una foto tomada que todavía no llegó al servidor.
class PendingUpload {
  const PendingUpload({
    required this.id,
    required this.path,
    required this.ownerType,
    required this.ownerId,
    required this.takenAt,
    this.caption,
    this.attempts = 0,
    this.lastError,
  });

  factory PendingUpload.fromJson(Map<String, dynamic> json) => PendingUpload(
        id: json['id'] as String,
        path: json['path'] as String,
        ownerType: MediaOwnerType.fromValue(json['ownerType'] as int),
        ownerId: json['ownerId'] as String,
        takenAt: DateTime.parse(json['takenAt'] as String),
        caption: json['caption'] as String?,
        attempts: json['attempts'] as int? ?? 0,
        lastError: json['lastError'] as String?,
      );

  final String id;

  /// Ruta de la copia local. La original del carrete puede desaparecer.
  final String path;
  final MediaOwnerType ownerType;
  final String ownerId;
  final DateTime takenAt;
  final String? caption;
  final int attempts;
  final String? lastError;

  Map<String, dynamic> toJson() => {
        'id': id,
        'path': path,
        'ownerType': ownerType.value,
        'ownerId': ownerId,
        'takenAt': takenAt.toIso8601String(),
        'caption': caption,
        'attempts': attempts,
        'lastError': lastError,
      };

  PendingUpload copyWith({int? attempts, String? lastError}) => PendingUpload(
        id: id,
        path: path,
        ownerType: ownerType,
        ownerId: ownerId,
        takenAt: takenAt,
        caption: caption,
        attempts: attempts ?? this.attempts,
        lastError: lastError,
      );
}

/// Fotos pendientes de subir, persistidas en disco.
///
/// Existe porque en un taller la cobertura es mala y el técnico no puede quedarse esperando
/// a que suba una foto para seguir trabajando: toma la foto, sigue, y la app la sube cuando
/// pueda. El estado vive en un JSON junto a las copias de las imágenes, así que sobrevive a
/// que el sistema mate la app —que es exactamente lo que pasa cuando se queda en segundo
/// plano subiendo—. No se usa una base de datos porque son unas pocas filas y un archivo se
/// entiende de un vistazo.
class UploadQueue extends AsyncNotifier<List<PendingUpload>> {
  static const _fileName = 'upload_queue.json';
  static const _folder = 'pending_uploads';

  /// Tras varios intentos fallidos deja de reintentar sola: si la foto es inválida o la
  /// orden ya no le corresponde al técnico, reintentar para siempre gasta batería y datos.
  static const maxAttempts = 5;

  bool _flushing = false;

  @override
  Future<List<PendingUpload>> build() => _read();

  Future<Directory> _directory() async {
    final base = await getApplicationDocumentsDirectory();
    final dir = Directory('${base.path}/$_folder');
    if (!dir.existsSync()) await dir.create(recursive: true);
    return dir;
  }

  Future<File> _manifest() async => File('${(await _directory()).path}/$_fileName');

  Future<List<PendingUpload>> _read() async {
    final file = await _manifest();
    if (!file.existsSync()) return [];

    try {
      final raw = jsonDecode(await file.readAsString()) as List<dynamic>;
      return raw.map((e) => PendingUpload.fromJson(e as Map<String, dynamic>)).toList();
    } catch (_) {
      // Un manifiesto corrupto no puede dejar la app sin poder tomar fotos nunca más.
      await file.delete();
      return [];
    }
  }

  Future<void> _write(List<PendingUpload> items) async {
    final file = await _manifest();
    await file.writeAsString(jsonEncode(items.map((e) => e.toJson()).toList()));
    state = AsyncData(items);
  }

  /// Guarda la foto y trata de subirla enseguida. Devuelve cuando está encolada, no cuando
  /// terminó de subir: el técnico sigue trabajando mientras tanto.
  Future<void> enqueue({
    required File photo,
    required MediaOwnerType ownerType,
    required String ownerId,
    required DateTime takenAt,
    String? caption,
  }) async {
    final dir = await _directory();
    final id = '${takenAt.microsecondsSinceEpoch}-${photo.path.hashCode.abs()}';
    final copy = await photo.copy('${dir.path}/$id.jpg');

    await _write([
      ...(state.value ?? []),
      PendingUpload(
        id: id,
        path: copy.path,
        ownerType: ownerType,
        ownerId: ownerId,
        takenAt: takenAt,
        caption: caption,
      ),
    ]);

    await flush();
  }

  /// Intenta subir todo lo pendiente. Es seguro llamarla de más: si ya hay un vaciado en
  /// curso, sale sin hacer nada en vez de subir la misma foto dos veces.
  Future<void> flush() async {
    if (_flushing) return;

    final pending = state.value ?? [];
    if (pending.isEmpty) return;

    _flushing = true;
    final repository = ref.read(mediaRepositoryProvider);
    var remaining = [...pending];

    try {
      for (final item in pending) {
        if (item.attempts >= maxAttempts) continue;

        final file = File(item.path);
        if (!file.existsSync()) {
          // El archivo se perdió: la entrada ya no sirve de nada.
          remaining.removeWhere((e) => e.id == item.id);
          continue;
        }

        try {
          await repository.upload(
            file: file,
            ownerType: item.ownerType,
            ownerId: item.ownerId,
            takenAt: item.takenAt,
            caption: item.caption,
          );

          await file.delete();
          remaining.removeWhere((e) => e.id == item.id);
        } catch (e) {
          final index = remaining.indexWhere((p) => p.id == item.id);
          if (index >= 0) {
            // Un 402 es la mensualidad vencida, y eso no se arregla esperando: se da por
            // agotada de una vez en vez de gastar los cinco intentos contra una pared.
            final vencido = e is DioException && e.response?.statusCode == 402;

            remaining[index] = item.copyWith(
              attempts: vencido ? maxAttempts : item.attempts + 1,
              lastError: vencido
                  ? 'La mensualidad de GarajApp está vencida.'
                  : e.toString(),
            );
          }
          // Si falló una, lo más probable es que no haya red: no tiene sentido
          // intentar las demás y acumular errores.
          break;
        }
      }
    } finally {
      _flushing = false;
      await _write(remaining);
    }
  }

  /// Descarta una foto que no se pudo subir. La decide el técnico, no la app.
  Future<void> discard(String id) async {
    final pending = state.value ?? [];
    final item = pending.where((e) => e.id == id).firstOrNull;
    if (item == null) return;

    final file = File(item.path);
    if (file.existsSync()) await file.delete();

    await _write(pending.where((e) => e.id != id).toList());
  }

  /// Vuelve a poner a cero los intentos de lo que se rindió, y reintenta.
  Future<void> retryAll() async {
    await _write([
      for (final item in state.value ?? []) item.copyWith(attempts: 0),
    ]);
    await flush();
  }
}

final uploadQueueProvider =
    AsyncNotifierProvider<UploadQueue, List<PendingUpload>>(UploadQueue.new);

/// Cuántas fotos de esta orden siguen esperando red.
final pendingUploadsForProvider = Provider.family<List<PendingUpload>, String>(
  (ref, ownerId) => (ref.watch(uploadQueueProvider).value ?? [])
      .where((p) => p.ownerId == ownerId)
      .toList(),
);
