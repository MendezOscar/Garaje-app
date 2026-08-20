import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/media.dart';

final mediaRepositoryProvider = Provider<MediaRepository>(
  (ref) => MediaRepository(ref.watch(apiClientProvider).dio),
);

class MediaRepository {
  MediaRepository(this._dio);

  final Dio _dio;

  Future<List<MediaAttachment>> listForWorkOrder(String workOrderId) async {
    final response = await _dio.get<List<dynamic>>('/api/media/work-order/$workOrderId');

    return response.data!
        .map((e) => MediaAttachment.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Las fotos de un dueño cualquiera: una cotización, un paso, una solicitud.
  Future<List<MediaAttachment>> list(MediaOwnerType ownerType, String ownerId) async {
    final response = await _dio.get<List<dynamic>>(
      '/api/media',
      queryParameters: {'ownerType': ownerType.value, 'ownerId': ownerId},
    );

    return response.data!
        .map((e) => MediaAttachment.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Sube el archivo en tres pasos: pedir URL, PUT directo al bucket y confirmar.
  ///
  /// El binario no pasa por la API. Importa aquí más que en el web: en el taller la subida
  /// puede tardar minutos, y así lo lento es solo la conexión con el bucket.
  Future<MediaAttachment> upload({
    required File file,
    required MediaOwnerType ownerType,
    required String ownerId,
    required DateTime takenAt,
    String? caption,
    String contentType = 'image/jpeg',
  }) async {
    final bytes = await file.length();

    final presigned = await _dio.post<Map<String, dynamic>>(
      '/api/media/upload-url',
      data: {
        'ownerType': ownerType.value,
        'ownerId': ownerId,
        'contentType': contentType,
        'sizeBytes': bytes,
        'fileName': file.uri.pathSegments.last,
        'caption': caption,
        'takenAt': takenAt.toUtc().toIso8601String(),
      },
    );

    final data = presigned.data!;
    final headers = (data['headers'] as Map<String, dynamic>).cast<String, String>();

    // Dio nuevo y sin interceptores: el de la app añadiría el Authorization del taller y S3
    // invalida la firma si llega esa cabecera de más.
    await Dio().put<void>(
      data['uploadUrl'] as String,
      data: file.openRead(),
      options: Options(
        headers: {...headers, Headers.contentLengthHeader: bytes},
        // Sube por 3G del taller: el timeout por defecto de un minuto se queda corto.
        sendTimeout: const Duration(minutes: 5),
      ),
    );

    final confirmed = await _dio.post<Map<String, dynamic>>(
      '/api/media/${data['attachmentId']}/confirm',
    );

    return MediaAttachment.fromJson(confirmed.data!);
  }

  Future<void> delete(String id) => _dio.delete<void>('/api/media/$id');
}

/// Galería de una orden. `autoDispose` para que al volver a la pantalla se recargue: las URL
/// prefirmadas caducan a los 15 minutos y una imagen cacheada dejaría de cargar.
final workOrderMediaProvider =
    FutureProvider.autoDispose.family<List<MediaAttachment>, String>(
  (ref, workOrderId) => ref.watch(mediaRepositoryProvider).listForWorkOrder(workOrderId),
);

/// Las fotos del daño de una cotización. Las sube el taller desde el panel y las mira el
/// cliente antes de aprobar: es lo que hace que un presupuesto se entienda sin ir al taller.
final quoteMediaProvider =
    FutureProvider.autoDispose.family<List<MediaAttachment>, String>(
  (ref, quoteId) =>
      ref.watch(mediaRepositoryProvider).list(MediaOwnerType.quote, quoteId),
);
