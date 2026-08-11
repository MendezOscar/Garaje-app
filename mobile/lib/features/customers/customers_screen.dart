import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/api/customer_repository.dart';
import '../../core/models/work_order.dart';

/// El padrón del taller. Es la libreta de clientes: quién es, qué anda, con qué teléfono se
/// le llama y si entra o no a la app.
///
/// Solo el Dueño: los datos del cliente y su acceso son suyos. El Técnico registra al que
/// llega al mostrador desde la recepción del vehículo, que es donde le hace falta.
class CustomersScreen extends ConsumerStatefulWidget {
  const CustomersScreen({super.key});

  @override
  ConsumerState<CustomersScreen> createState() => _CustomersScreenState();
}

class _CustomersScreenState extends ConsumerState<CustomersScreen> {
  String _search = '';

  Future<void> _edit([Customer? customer]) async {
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => _CustomerForm(customer: customer),
    );

    if (saved == true) ref.invalidate(customerSearchProvider(_search));
  }

  Future<void> _open(Customer customer) async {
    final changed = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => _CustomerSheet(customer: customer),
    );

    if (changed == true) ref.invalidate(customerSearchProvider(_search));
  }

  @override
  Widget build(BuildContext context) {
    final customers = ref.watch(customerSearchProvider(_search));

    return Scaffold(
      appBar: AppBar(title: const Text('Clientes')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _edit(),
        icon: const Icon(Icons.person_add_alt),
        label: const Text('Nuevo cliente'),
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 12, 12, 4),
            child: TextField(
              decoration: const InputDecoration(
                hintText: 'Buscar por nombre, teléfono o placa',
                prefixIcon: Icon(Icons.search),
                isDense: true,
              ),
              onChanged: (value) => setState(() => _search = value),
            ),
          ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async => ref.invalidate(customerSearchProvider(_search)),
              child: customers.when(
                loading: () => const Center(child: CircularProgressIndicator()),
                error: (e, _) => ListView(
                  children: [
                    const SizedBox(height: 100),
                    Center(
                      child: Padding(
                        padding: const EdgeInsets.all(24),
                        child: Text(apiErrorMessage(e, 'No se pudieron cargar los clientes.')),
                      ),
                    ),
                  ],
                ),
                data: (list) => list.isEmpty
                    ? ListView(
                        children: const [
                          SizedBox(height: 100),
                          Center(
                            child: Padding(
                              padding: EdgeInsets.all(24),
                              child: Text('Nadie con ese nombre, teléfono ni placa.'),
                            ),
                          ),
                        ],
                      )
                    : ListView.separated(
                        padding: const EdgeInsets.fromLTRB(12, 4, 12, 88),
                        itemCount: list.length,
                        separatorBuilder: (_, __) => const SizedBox(height: 8),
                        itemBuilder: (context, i) => _CustomerCard(
                          customer: list[i],
                          onTap: () => _open(list[i]),
                        ),
                      ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _CustomerCard extends StatelessWidget {
  const _CustomerCard({required this.customer, required this.onTap});

  final Customer customer;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(customer.fullName, style: theme.textTheme.titleSmall),
                    const SizedBox(height: 2),
                    Text(customer.phone, style: theme.textTheme.bodySmall),
                    Text(
                      customer.vehicleCount == 1
                          ? '1 vehículo'
                          : '${customer.vehicleCount} vehículos',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              if (customer.hasAppAccess)
                Tooltip(
                  message: customer.appUserEmail ?? 'Entra a la app',
                  child: Icon(
                    Icons.phone_iphone,
                    size: 20,
                    color: theme.colorScheme.primary,
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// La ficha: sus vehículos, cómo llamarlo y el acceso a la app.
class _CustomerSheet extends ConsumerStatefulWidget {
  const _CustomerSheet({required this.customer});

  final Customer customer;

  @override
  ConsumerState<_CustomerSheet> createState() => _CustomerSheetState();
}

class _CustomerSheetState extends ConsumerState<_CustomerSheet> {
  Future<void> _call() async {
    final phone = widget.customer.phone.replaceAll(RegExp(r'[^0-9+]'), '');
    await launchUrl(Uri.parse('tel:$phone'));
  }

  Future<void> _whatsapp() async {
    final phone = widget.customer.phone.replaceAll(RegExp(r'[^0-9]'), '');
    // Honduras es +504 y los teléfonos se guardan sin código: wa.me lo exige completo.
    final full = phone.length == 8 ? '504$phone' : phone;
    await launchUrl(Uri.parse('https://wa.me/$full'), mode: LaunchMode.externalApplication);
  }

  Future<void> _edit() async {
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => _CustomerForm(customer: widget.customer),
    );

    // Se cierra la ficha con el cambio hecho: sus datos ya no son los que tiene en memoria.
    if (saved == true && mounted) Navigator.pop(context, true);
  }

  /// Le abre el acceso a la app. Se hace desde su ficha y nunca al revés: así todo usuario
  /// con perfil Cliente corresponde a alguien del padrón.
  Future<void> _grantAccess() async {
    final email = TextEditingController(text: widget.customer.email ?? '');
    final password = TextEditingController();

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Darle acceso a la app'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Text(
              'Podrá seguir la reparación de sus vehículos y aprobar cotizaciones. '
              'Nada más.',
            ),
            const SizedBox(height: 12),
            TextField(
              controller: email,
              autofocus: true,
              keyboardType: TextInputType.emailAddress,
              autocorrect: false,
              decoration: const InputDecoration(labelText: 'Correo'),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: password,
              decoration: const InputDecoration(
                labelText: 'Contraseña',
                helperText: 'Mínimo 8 caracteres. Désela al cliente.',
              ),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Dar acceso'),
          ),
        ],
      ),
    );

    if (ok != true) return;

    try {
      await ref.read(customerRepositoryProvider).grantAppAccess(
            widget.customer.id,
            email.text.trim(),
            password.text,
          );

      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e))),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final customer = widget.customer;
    final vehicles = ref.watch(customerVehiclesProvider(customer.id));

    return SizedBox(
      height: MediaQuery.of(context).size.height * 0.75,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          ListTile(
            title: Text(customer.fullName, style: theme.textTheme.titleMedium),
            subtitle: Text(
              [
                customer.phone,
                if (customer.email != null) customer.email!,
                if (customer.taxId != null) 'RTN ${customer.taxId!}',
                if (customer.address != null) customer.address!,
              ].join(' · '),
            ),
            trailing: IconButton(
              tooltip: 'Editar',
              icon: const Icon(Icons.edit_outlined),
              onPressed: _edit,
            ),
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12),
            child: Row(
              children: [
                TextButton.icon(
                  onPressed: _call,
                  icon: const Icon(Icons.call_outlined, size: 18),
                  label: const Text('Llamar'),
                ),
                const SizedBox(width: 8),
                TextButton.icon(
                  onPressed: _whatsapp,
                  icon: const Icon(Icons.chat_outlined, size: 18),
                  label: const Text('WhatsApp'),
                ),
              ],
            ),
          ),
          const Divider(height: 1),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
              children: [
                Text(
                  'VEHÍCULOS',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                    letterSpacing: 0.6,
                  ),
                ),
                ...vehicles.maybeWhen(
                  data: (list) => list.isEmpty
                      ? [const ListTile(dense: true, title: Text('Ninguno registrado.'))]
                      : [
                          for (final vehicle in list)
                            ListTile(
                              dense: true,
                              contentPadding: EdgeInsets.zero,
                              leading: Icon(
                                vehicle.type == VehicleType.motorcycle
                                    ? Icons.two_wheeler
                                    : Icons.directions_car,
                              ),
                              title: Text(vehicle.label),
                              subtitle: Text(
                                [
                                  if (vehicle.plate != null) vehicle.plate!,
                                  if (vehicle.mileage != null) '${vehicle.mileage} km',
                                ].join(' · '),
                              ),
                            ),
                        ],
                  orElse: () => [const LinearProgressIndicator()],
                ),
                const SizedBox(height: 16),
                Text(
                  'ACCESO A LA APP',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                    letterSpacing: 0.6,
                  ),
                ),
                const SizedBox(height: 4),
                if (customer.hasAppAccess)
                  Text(
                    'Entra con ${customer.appUserEmail}. La contraseña se cambia desde '
                    'Usuarios.',
                    style: theme.textTheme.bodyMedium,
                  )
                else ...[
                  Text(
                    'No tiene. Es opcional: la mayoría de los clientes nunca lo pide.',
                    style: theme.textTheme.bodySmall,
                  ),
                  const SizedBox(height: 8),
                  FilledButton.tonalIcon(
                    onPressed: _grantAccess,
                    icon: const Icon(Icons.phone_iphone, size: 18),
                    label: const Text('Darle acceso'),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

}

class _CustomerForm extends ConsumerStatefulWidget {
  const _CustomerForm({this.customer});

  final Customer? customer;

  @override
  ConsumerState<_CustomerForm> createState() => _CustomerFormState();
}

class _CustomerFormState extends ConsumerState<_CustomerForm> {
  late final _name = TextEditingController(text: widget.customer?.fullName ?? '');
  late final _phone = TextEditingController(text: widget.customer?.phone ?? '');
  late final _email = TextEditingController(text: widget.customer?.email ?? '');
  late final _taxId = TextEditingController(text: widget.customer?.taxId ?? '');
  late final _address = TextEditingController(text: widget.customer?.address ?? '');
  late final _notes = TextEditingController(text: widget.customer?.notes ?? '');

  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    for (final c in [_name, _phone, _email, _taxId, _address, _notes]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    if (_name.text.trim().isEmpty || _phone.text.trim().isEmpty) {
      setState(() => _error = 'El nombre y el teléfono son obligatorios.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await ref.read(customerRepositoryProvider).save(
            id: widget.customer?.id,
            fullName: _name.text.trim(),
            phone: _phone.text.trim(),
            email: _email.text.trim().isEmpty ? null : _email.text.trim(),
            taxId: _taxId.text.trim().isEmpty ? null : _taxId.text.trim(),
            address: _address.text.trim().isEmpty ? null : _address.text.trim(),
            notes: _notes.text.trim().isEmpty ? null : _notes.text.trim(),
          );

      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      setState(() => _error = apiErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        bottom: MediaQuery.of(context).viewInsets.bottom + 16,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              widget.customer == null ? 'Nuevo cliente' : 'Editar cliente',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _name,
              textCapitalization: TextCapitalization.words,
              decoration: const InputDecoration(labelText: 'Nombre completo'),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _phone,
              keyboardType: TextInputType.phone,
              decoration: const InputDecoration(
                labelText: 'Teléfono',
                helperText: 'Es por donde se le manda la cotización.',
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _email,
              keyboardType: TextInputType.emailAddress,
              autocorrect: false,
              decoration: const InputDecoration(labelText: 'Correo (opcional)'),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _taxId,
              keyboardType: TextInputType.number,
              decoration: const InputDecoration(
                labelText: 'RTN (opcional)',
                helperText: 'Para la factura con CAI.',
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _address,
              decoration: const InputDecoration(labelText: 'Dirección (opcional)'),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _notes,
              maxLines: 2,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Notas (opcional)'),
            ),
            if (_error != null) ...[
              const SizedBox(height: 12),
              Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
            const SizedBox(height: 16),
            FilledButton(
              onPressed: _busy ? null : _save,
              child: Text(widget.customer == null ? 'Crear' : 'Guardar'),
            ),
          ],
        ),
      ),
    );
  }
}
