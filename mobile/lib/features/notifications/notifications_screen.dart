import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/notification_repository.dart';
import '../../core/models/notification.dart';
import '../../core/push/push_messaging.dart';

/// Los avisos del usuario. Tocar uno lo marca leído y lleva a la orden, que es donde está
/// todo lo demás: el vehículo, los pasos, las fotos y la cotización.
class NotificationsScreen extends ConsumerWidget {
  const NotificationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifications = ref.watch(notificationsProvider);

    // Un botón que no puede hacer nada se lee como función a medio terminar.
    final hayAvisos = notifications.asData?.value.isNotEmpty ?? false;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Avisos'),
        actions: [
          if (hayAvisos)
            TextButton(
              onPressed: () async {
                await ref.read(notificationRepositoryProvider).markAllRead();
                ref
                  ..invalidate(notificationsProvider)
                  ..invalidate(unreadCountProvider);
              },
              child: const Text('Marcar todo'),
            ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(notificationsProvider),
        child: Column(
          children: [
            const _AvisosApagados(),
            Expanded(
              child: notifications.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(
            children: [
              const SizedBox(height: 120),
              Center(child: Text(apiErrorMessage(e, 'No se pudieron cargar los avisos.'))),
            ],
          ),
          data: (items) => items.isEmpty
              ? ListView(
                  children: const [
                    SizedBox(height: 120),
                    Center(child: Text('No hay avisos todavía.')),
                  ],
                )
              : ListView.separated(
                  itemCount: items.length,
                  separatorBuilder: (_, __) => const Divider(height: 1),
                  itemBuilder: (context, i) => _NotificationTile(notification: items[i]),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Si el teléfono tiene los avisos apagados, los de la campana siguen llegando pero no suenan.
/// Sin decirlo, quien dijo «ahora no» al entrar cree que la aplicación está rota.
class _AvisosApagados extends ConsumerWidget {
  const _AvisosApagados();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final activos = ref.watch(avisosDelSistemaProvider);
    if (activos.asData?.value ?? true) return const SizedBox.shrink();

    final theme = Theme.of(context);

    return Container(
      width: double.infinity,
      color: theme.colorScheme.surfaceContainerHighest,
      padding: const EdgeInsets.fromLTRB(16, 12, 8, 12),
      child: Row(
        children: [
          Expanded(
            child: Text(
              'Su teléfono no va a sonar con los avisos. Aquí los sigue viendo.',
              style: theme.textTheme.bodySmall,
            ),
          ),
          TextButton(
            onPressed: () async {
              await ref.read(pushMessagingProvider).pedirPermisoOtraVez();
              ref.invalidate(avisosDelSistemaProvider);
            },
            child: const Text('Activar'),
          ),
        ],
      ),
    );
  }
}

class _NotificationTile extends ConsumerWidget {
  const _NotificationTile({required this.notification});

  final AppNotification notification;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return ListTile(
      leading: Text(notification.type.icon, style: const TextStyle(fontSize: 22)),
      title: Text(
        notification.title,
        style: TextStyle(
          fontWeight: notification.isRead ? FontWeight.normal : FontWeight.w600,
        ),
      ),
      subtitle: Text('${notification.body}\n${_relative(notification.createdAt)}'),
      isThreeLine: true,
      tileColor: notification.isRead ? null : Theme.of(context).colorScheme.surfaceContainerHighest,
      onTap: () async {
        if (!notification.isRead) {
          await ref.read(notificationRepositoryProvider).markRead(notification.id);
          ref
            ..invalidate(notificationsProvider)
            ..invalidate(unreadCountProvider);
        }

        if (notification.workOrderId case final id? when context.mounted) {
          context.push('/ordenes/$id');
        }
      },
    );
  }
}

String _relative(DateTime when) {
  final minutes = DateTime.now().difference(when).inMinutes;

  if (minutes < 1) return 'ahora';
  if (minutes < 60) return 'hace $minutes min';
  if (minutes < 1440) return 'hace ${minutes ~/ 60} h';
  return 'hace ${minutes ~/ 1440} d';
}

/// Campana con el globo de no leídos, para la barra de las pantallas principales.
class NotificationBell extends ConsumerWidget {
  const NotificationBell({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final unread = ref.watch(unreadCountProvider).value ?? 0;

    return IconButton(
      tooltip: 'Avisos',
      onPressed: () => context.push('/avisos'),
      icon: Badge(
        isLabelVisible: unread > 0,
        label: Text(unread > 9 ? '9+' : '$unread'),
        child: const Icon(Icons.notifications_outlined),
      ),
    );
  }
}
