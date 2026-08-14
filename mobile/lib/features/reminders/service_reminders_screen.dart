import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/api/work_order_repository.dart';

/// A quién le toca servicio.
///
/// Es trabajo que hoy se pierde por no acordarse: el cliente vuelve cuando algo suena, no
/// cuando le toca. La lista sale de lo que el taller anotó al entregar cada vehículo, y se cae
/// sola cuando el carro vuelve.
///
/// Está en el teléfono porque llamar a los clientes es lo que se hace en los ratos muertos del
/// taller, no sentado frente a una computadora.
class ServiceRemindersScreen extends ConsumerStatefulWidget {
  const ServiceRemindersScreen({super.key});

  @override
  ConsumerState<ServiceRemindersScreen> createState() => _ServiceRemindersScreenState();
}

class _ServiceRemindersScreenState extends ConsumerState<ServiceRemindersScreen> {
  ReminderFilter _filter = ReminderFilter.month;

  final _search = TextEditingController();
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    _search.text = ref.read(remindersSearchProvider);
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  /// Se espera a que deje de escribir: cada pulsación sería una consulta a la API.
  void _onSearch(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), () {
      if (mounted) ref.read(remindersSearchProvider.notifier).set(value);
    });
  }

  /// Manda el recordatorio y recarga: el que ya se avisó sale de la lista, que es lo que evita
  /// llamar dos veces al mismo cliente.
  Future<void> _remind(ServiceReminder reminder) async {
    try {
      final url = await ref
          .read(workOrderRepositoryProvider)
          .serviceReminderLink(reminder.workOrderId);

      final launched = await launchUrl(Uri.parse(url), mode: LaunchMode.externalApplication);
      if (!launched && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('No se pudo abrir WhatsApp.')),
        );
      }

      ref.invalidate(serviceRemindersProvider(_filter));
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(apiErrorMessage(e))),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final reminders = ref.watch(serviceRemindersProvider(_filter));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Recordatorios'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(112),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
            child: Column(
              children: [
                TextField(
                  controller: _search,
                  onChanged: _onSearch,
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    hintText: 'Cliente, teléfono o placa',
                    prefixIcon: const Icon(Icons.search),
                    isDense: true,
                    border: const OutlineInputBorder(),
                    suffixIcon: _search.text.isEmpty
                        ? null
                        : IconButton(
                            icon: const Icon(Icons.close),
                            onPressed: () {
                              _search.clear();
                              _onSearch('');
                            },
                          ),
                  ),
                ),
                const SizedBox(height: 8),
                SizedBox(
                  height: 40,
                  child: ListView(
                    scrollDirection: Axis.horizontal,
                    children: [
                      _Chip(
                        label: 'Este mes',
                        selected: _filter == ReminderFilter.month,
                        onSelected: () => setState(() => _filter = ReminderFilter.month),
                      ),
                      _Chip(
                        label: 'Ya les tocaba',
                        selected: _filter == ReminderFilter.overdue,
                        onSelected: () => setState(() => _filter = ReminderFilter.overdue),
                      ),
                      _Chip(
                        label: 'Ya recordados',
                        selected: _filter == ReminderFilter.reminded,
                        onSelected: () => setState(() => _filter = ReminderFilter.reminded),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(serviceRemindersProvider(_filter)),
        child: reminders.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(
            padding: const EdgeInsets.all(24),
            children: [Text(apiErrorMessage(e, 'No se pudo cargar a quién le toca servicio.'))],
          ),
          data: (list) {
            if (list.isEmpty) {
              return ListView(
                padding: const EdgeInsets.all(24),
                children: [
                  Text(
                    _filter == ReminderFilter.month && _search.text.isEmpty
                        ? 'Nadie tiene servicio pendiente este mes.'
                        : 'Nada con esos filtros.',
                    style: theme.textTheme.bodyMedium,
                  ),
                ],
              );
            }

            return ListView.builder(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              itemCount: list.length,
              itemBuilder: (_, i) => _ReminderCard(
                reminder: list[i],
                onRemind: () => _remind(list[i]),
              ),
            );
          },
        ),
      ),
    );
  }
}

class _ReminderCard extends StatelessWidget {
  const _ReminderCard({required this.reminder, required this.onRemind});

  final ServiceReminder reminder;
  final VoidCallback onRemind;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final cuando = reminder.daysUntil < 0
        ? 'le tocaba hace ${reminder.daysUntil.abs()} '
            '${reminder.daysUntil.abs() == 1 ? 'día' : 'días'}'
        : reminder.daysUntil == 0
            ? 'le toca hoy'
            : 'le toca en ${reminder.daysUntil} '
                '${reminder.daysUntil == 1 ? 'día' : 'días'}';

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(reminder.customerName, style: theme.textTheme.titleSmall),
                      Text(
                        '${reminder.vehicleLabel}'
                        '${reminder.plate == null ? '' : ' · ${reminder.plate}'}',
                        style: theme.textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
                Text(
                  cuando,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: reminder.isOverdue ? theme.colorScheme.error : null,
                    fontWeight: reminder.isOverdue ? FontWeight.w600 : null,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 6),
            if (reminder.nextServiceMileage != null)
              Text(
                'A los ${_km(reminder.nextServiceMileage!)} km'
                '${reminder.lastMileage == null ? '' : ' · última lectura ${_km(reminder.lastMileage!)}'}',
                style: theme.textTheme.bodySmall,
              ),
            // De qué hablarle cuando conteste: lo que se le hizo la última vez.
            Text(
              '${reminder.lastService} · ${reminder.orderNumber}',
              style: theme.textTheme.bodySmall,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
            if (reminder.remindedAt != null)
              Text('Ya se le recordó', style: theme.textTheme.bodySmall),
            const SizedBox(height: 8),
            // A lo ancho, como el resto de los botones de la app: el tema les pone ancho
            // infinito por mínimo, así que dentro de una `Row` reventarían el layout.
            SizedBox(
              width: double.infinity,
              child: FilledButton.tonalIcon(
                onPressed: onRemind,
                icon: const Icon(Icons.chat_outlined, size: 18),
                label: const Text('Recordar por WhatsApp'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Kilómetros con separador de miles: «61,000» se lee de un golpe y «61000» no.
String _km(int value) {
  final texto = value.toString();
  final buffer = StringBuffer();

  for (var i = 0; i < texto.length; i++) {
    if (i > 0 && (texto.length - i) % 3 == 0) buffer.write(',');
    buffer.write(texto[i]);
  }

  return buffer.toString();
}

class _Chip extends StatelessWidget {
  const _Chip({required this.label, required this.selected, required this.onSelected});

  final String label;
  final bool selected;
  final VoidCallback onSelected;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(right: 8),
        child: ChoiceChip(
          label: Text(label),
          selected: selected,
          onSelected: (_) => onSelected(),
        ),
      );
}
