import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/api/api_client.dart';
import '../../core/api/media_repository.dart';
import '../../core/models/media.dart';
import '../../core/sync/upload_queue.dart';
import 'photo_capture.dart';

/// Fotos de lo que se documenta: el proceso de una orden o el daño de una cotización.
///
/// La misma galería para las dos porque es el mismo gesto —apuntar y disparar con el vehículo
/// delante— y el mismo problema: la señal del taller. Lo único que cambia es de quién son las
/// fotos y el rótulo.
class PhotoGallery extends ConsumerStatefulWidget {
  const PhotoGallery({
    required this.ownerId,
    required this.canEdit,
    this.ownerType = MediaOwnerType.workOrder,
    this.titulo = 'FOTOS DEL PROCESO',
    this.vacioPropio = 'Todavía no hay fotos. Tome una para documentar el proceso.',
    this.vacioAjeno = 'El taller todavía no ha subido fotos.',
    super.key,
  });

  final String ownerId;
  final MediaOwnerType ownerType;
  final String titulo;

  /// Qué decir cuando no hay fotos: a quien puede tomarlas y a quien solo las mira.
  final String vacioPropio;
  final String vacioAjeno;
  final bool canEdit;

  @override
  ConsumerState<PhotoGallery> createState() => _PhotoGalleryState();
}

class _PhotoGalleryState extends ConsumerState<PhotoGallery> {
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    // Al abrir la orden se reintenta lo que quedó pendiente: es el momento en que el
    // técnico suele volver a tener red, al salir del foso o del sótano del taller.
    WidgetsBinding.instance.addPostFrameCallback((_) => _flush());
  }

  FutureProvider<List<MediaAttachment>> get _fotos =>
      mediaProviderDe(widget.ownerType, widget.ownerId);

  Future<void> _flush() async {
    await ref.read(uploadQueueProvider.notifier).flush();
    if (mounted) ref.invalidate(_fotos);
  }

  Future<void> _takePhoto(ImageSource source) async {
    setState(() => _busy = true);
    try {
      await capturarFoto(
        ref,
        ownerId: widget.ownerId,
        ownerType: widget.ownerType,
        source: source,
      );
    } catch (e) {
      _snack(apiErrorMessage(e, 'No se pudo guardar la foto.'));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _delete(MediaAttachment photo) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('¿Eliminar la foto?'),
        content: const Text('No se puede deshacer.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancelar')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Eliminar')),
        ],
      ),
    );

    if (confirmed != true) return;

    try {
      await ref.read(mediaRepositoryProvider).delete(photo.id);
      ref.invalidate(_fotos);
    } catch (e) {
      _snack(apiErrorMessage(e, 'No se pudo eliminar la foto.'));
    }
  }

  void _snack(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  void _open(MediaAttachment photo) {
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => _PhotoViewer(
          photo: photo,
          onDelete: widget.canEdit ? () => _delete(photo) : null,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final media = ref.watch(_fotos);
    final pending = ref.watch(pendingUploadsForProvider(widget.ownerId));

    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  widget.titulo,
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                    letterSpacing: 0.6,
                  ),
                ),
              ),
              if (widget.canEdit) ...[
                IconButton(
                  onPressed: _busy ? null : () => _takePhoto(ImageSource.gallery),
                  icon: const Icon(Icons.photo_library_outlined),
                  tooltip: 'Elegir de la galería',
                ),
                IconButton.filledTonal(
                  onPressed: _busy ? null : () => _takePhoto(ImageSource.camera),
                  icon: const Icon(Icons.photo_camera_outlined),
                  tooltip: 'Tomar foto',
                ),
              ],
            ],
          ),
          const SizedBox(height: 8),

          if (pending.isNotEmpty) _PendingBanner(pending: pending, onRetry: _flush),

          media.when(
            loading: () => const Padding(
              padding: EdgeInsets.symmetric(vertical: 12),
              child: SizedBox(
                height: 2,
                child: LinearProgressIndicator(),
              ),
            ),
            error: (e, _) => Text(
              apiErrorMessage(e, 'No se pudieron cargar las fotos.'),
              style: theme.textTheme.bodySmall,
            ),
            data: (photos) => photos.isEmpty && pending.isEmpty
                ? Text(
                    widget.canEdit ? widget.vacioPropio : widget.vacioAjeno,
                    style: theme.textTheme.bodySmall,
                  )
                : GridView.count(
                    crossAxisCount: 3,
                    mainAxisSpacing: 6,
                    crossAxisSpacing: 6,
                    shrinkWrap: true,
                    physics: const NeverScrollableScrollPhysics(),
                    children: [
                      for (final photo in photos)
                        _Thumbnail(photo: photo, onTap: () => _open(photo)),
                    ],
                  ),
          ),
        ],
      ),
    );
  }
}

class _Thumbnail extends StatelessWidget {
  const _Thumbnail({required this.photo, required this.onTap});

  final MediaAttachment photo;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(8),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(8),
        child: Stack(
          fit: StackFit.expand,
          children: [
            Image.network(
              photo.thumbnailUrl,
              fit: BoxFit.cover,
              errorBuilder: (_, __, ___) => Container(
                color: theme.colorScheme.surfaceContainerHighest,
                child: Icon(Icons.broken_image_outlined, color: theme.colorScheme.outline),
              ),
            ),
            if (!photo.isVisibleToCustomer)
              const Positioned(
                top: 4,
                right: 4,
                child: Icon(Icons.visibility_off, size: 14, color: Colors.white),
              ),
          ],
        ),
      ),
    );
  }
}

/// Fotos tomadas que siguen esperando red. Se muestran para que el técnico sepa que no se
/// perdieron: sin esto, tomar una foto sin señal parece que no hizo nada.
class _PendingBanner extends StatelessWidget {
  const _PendingBanner({required this.pending, required this.onRetry});

  final List<PendingUpload> pending;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final stuck = pending.where((p) => p.attempts >= UploadQueue.maxAttempts).length;

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      color: theme.colorScheme.surfaceContainerHighest,
      child: ListTile(
        dense: true,
        leading: const Icon(Icons.cloud_upload_outlined),
        title: Text(
          pending.length == 1
              ? '1 foto pendiente de subir'
              : '${pending.length} fotos pendientes de subir',
          style: theme.textTheme.bodyMedium,
        ),
        subtitle: Text(
          stuck > 0 ? 'Se subirán cuando haya conexión.' : 'Subiendo…',
          style: theme.textTheme.bodySmall,
        ),
        trailing: TextButton(onPressed: onRetry, child: const Text('Reintentar')),
      ),
    );
  }
}

class _PhotoViewer extends StatelessWidget {
  const _PhotoViewer({required this.photo, this.onDelete});

  final MediaAttachment photo;
  final VoidCallback? onDelete;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: Text(photo.taskTitle ?? 'Foto'),
        actions: [
          if (onDelete != null)
            IconButton(
              onPressed: () {
                Navigator.pop(context);
                onDelete!();
              },
              icon: const Icon(Icons.delete_outline),
            ),
        ],
      ),
      body: Column(
        children: [
          Expanded(
            child: InteractiveViewer(
              maxScale: 5,
              child: Center(child: Image.network(photo.url, fit: BoxFit.contain)),
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (photo.caption != null)
                  Text(photo.caption!, style: const TextStyle(color: Colors.white)),
                Text(
                  '${photo.uploadedByName} · ${_formatDate(photo.takenAt)}',
                  style: TextStyle(color: Colors.white.withValues(alpha: 0.7), fontSize: 12),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  static String _formatDate(DateTime value) {
    final local = value.toLocal();
    final d = local.day.toString().padLeft(2, '0');
    final m = local.month.toString().padLeft(2, '0');
    final h = local.hour.toString().padLeft(2, '0');
    final min = local.minute.toString().padLeft(2, '0');
    return '$d/$m $h:$min';
  }
}
