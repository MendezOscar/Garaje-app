import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/service_request_repository.dart';
import '../../core/api/staff_repository.dart';

/// Usuarios del taller, para el Dueño.
///
/// Es donde da de alta a un técnico nuevo, le cambia la contraseña cuando la olvida y lo da
/// de baja cuando deja de trabajar. Se hace desde el teléfono a propósito: el Dueño de un
/// taller pequeño no está frente a una computadora cuando llega el técnico nuevo.
///
/// El acceso de los clientes no se crea aquí, sino al registrarlos: así todo acceso
/// corresponde a alguien del padrón.
class UsersScreen extends ConsumerStatefulWidget {
  const UsersScreen({super.key});

  @override
  ConsumerState<UsersScreen> createState() => _UsersScreenState();
}

class _UsersScreenState extends ConsumerState<UsersScreen> {
  String? _busyId;

  Future<void> _run(Future<void> Function() action, String message) async {
    try {
      await action();
      ref.invalidate(staffUsersProvider);
      ref.invalidate(technicianOptionsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(apiErrorMessage(e))),
        );
      }
    } finally {
      if (mounted) setState(() => _busyId = null);
    }
  }

  Future<void> _create() async {
    final branches = ref.read(branchOptionsProvider).value ?? const <BranchOption>[];
    if (branches.isEmpty) return;

    final draft = await showModalBottomSheet<_TechnicianDraft>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _NewTechnicianSheet(branches: branches),
    );

    if (draft == null) return;

    setState(() => _busyId = 'new');
    await _run(
      () => ref.read(staffRepositoryProvider).createTechnician(
            email: draft.email,
            fullName: draft.fullName,
            password: draft.password,
            branchIds: draft.branchIds,
          ),
      'Técnico creado. Entréguele el correo y la contraseña.',
    );
  }

  Future<void> _resetPassword(StaffUser user) async {
    final password = await _askPassword(user);
    if (password == null) return;

    setState(() => _busyId = user.id);
    await _run(
      () => ref.read(staffRepositoryProvider).resetPassword(user.id, password),
      'Contraseña cambiada. Sus sesiones abiertas se cerraron.',
    );
  }

  Future<void> _toggleActive(StaffUser user) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(user.isActive ? 'Dar de baja' : 'Reactivar'),
        content: Text(
          user.isActive
              ? '${user.fullName} dejará de poder entrar. Su trabajo queda registrado.'
              : '${user.fullName} podrá volver a entrar con su misma contraseña.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancelar'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text(user.isActive ? 'Dar de baja' : 'Reactivar'),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    setState(() => _busyId = user.id);
    await _run(
      () => ref.read(staffRepositoryProvider).updateUser(user, isActive: !user.isActive),
      user.isActive ? 'Dado de baja.' : 'Reactivado.',
    );
  }

  Future<String?> _askPassword(StaffUser user) {
    final controller = TextEditingController();

    return showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(user.fullName),
        content: TextField(
          controller: controller,
          autofocus: true,
          decoration: const InputDecoration(
            labelText: 'Nueva contraseña',
            helperText: 'Mínimo 8 caracteres. Se la entrega usted.',
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(
            onPressed: () {
              final value = controller.text.trim();
              if (value.length >= 8) Navigator.pop(context, value);
            },
            child: const Text('Cambiar'),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final users = ref.watch(staffUsersProvider);
    // Hacen falta para el alta: un técnico sin sucursal no vería ninguna orden.
    ref.watch(branchOptionsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Usuarios')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _busyId != null ? null : _create,
        icon: const Icon(Icons.person_add_alt),
        label: const Text('Nuevo técnico'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(staffUsersProvider),
        child: users.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(
            children: [
              const SizedBox(height: 120),
              Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text(
                    apiErrorMessage(e, 'No se pudieron cargar los usuarios.'),
                    textAlign: TextAlign.center,
                  ),
                ),
              ),
            ],
          ),
          data: (list) {
            final technicians = list.where((u) => u.isTechnician).toList();
            final others = list.where((u) => !u.isTechnician).toList();

            return ListView(
              // Hueco abajo para que el botón flotante no tape la última tarjeta.
              padding: const EdgeInsets.fromLTRB(12, 12, 12, 88),
              children: [
                _GroupTitle(
                  'Técnicos',
                  subtitle: technicians.isEmpty ? 'Todavía no hay ninguno.' : null,
                ),
                for (final user in technicians)
                  _UserCard(
                    user: user,
                    busy: _busyId == user.id,
                    onPassword: () => _resetPassword(user),
                    onToggle: () => _toggleActive(user),
                  ),
                const _GroupTitle('Dueños y clientes'),
                for (final user in others)
                  _UserCard(
                    user: user,
                    busy: _busyId == user.id,
                    onPassword: () => _resetPassword(user),
                    onToggle: user.role == 'Owner' ? null : () => _toggleActive(user),
                  ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _GroupTitle extends StatelessWidget {
  const _GroupTitle(this.title, {this.subtitle});

  final String title;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(top: 8, bottom: 4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title.toUpperCase(),
            style: theme.textTheme.labelSmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
              letterSpacing: 0.6,
            ),
          ),
          if (subtitle != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(subtitle!, style: theme.textTheme.bodySmall),
            ),
        ],
      ),
    );
  }
}

class _UserCard extends StatelessWidget {
  const _UserCard({
    required this.user,
    required this.busy,
    required this.onPassword,
    this.onToggle,
  });

  final StaffUser user;
  final bool busy;
  final VoidCallback onPassword;
  final VoidCallback? onToggle;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: Opacity(
        opacity: user.isActive ? 1 : 0.55,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 8, 8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(user.fullName, style: theme.textTheme.titleSmall),
                  ),
                  if (!user.isActive)
                    Text('De baja', style: theme.textTheme.labelSmall),
                ],
              ),
              Text(user.email, style: theme.textTheme.bodySmall),
              Text(
                user.lastLoginAt == null
                    ? 'Nunca ha entrado'
                    : 'Último ingreso ${_date(user.lastLoginAt!)}',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  TextButton(
                    onPressed: busy ? null : onPassword,
                    child: const Text('Contraseña'),
                  ),
                  if (onToggle != null)
                    TextButton(
                      onPressed: busy ? null : onToggle,
                      child: Text(user.isActive ? 'Dar de baja' : 'Reactivar'),
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  static String _date(DateTime value) {
    final local = value.toLocal();
    return '${local.day}/${local.month}/${local.year}';
  }
}

class _TechnicianDraft {
  const _TechnicianDraft({
    required this.email,
    required this.fullName,
    required this.password,
    required this.branchIds,
  });

  final String email;
  final String fullName;
  final String password;
  final List<String> branchIds;
}

class _NewTechnicianSheet extends StatefulWidget {
  const _NewTechnicianSheet({required this.branches});

  final List<BranchOption> branches;

  @override
  State<_NewTechnicianSheet> createState() => _NewTechnicianSheetState();
}

class _NewTechnicianSheetState extends State<_NewTechnicianSheet> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _branchIds = <String>{};

  @override
  void initState() {
    super.initState();
    // Con una sola sucursal no hay nada que elegir: se marca sola.
    if (widget.branches.length == 1) _branchIds.add(widget.branches.first.id);
  }

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  void _submit() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    if (_branchIds.isEmpty) return;

    Navigator.pop(
      context,
      _TechnicianDraft(
        email: _email.text.trim(),
        fullName: _name.text.trim(),
        password: _password.text.trim(),
        branchIds: _branchIds.toList(),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
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
            Text('Técnico nuevo', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 12),

            TextFormField(
              controller: _name,
              decoration: const InputDecoration(labelText: 'Nombre completo'),
              textCapitalization: TextCapitalization.words,
              validator: (v) => v == null || v.trim().isEmpty ? 'Falta el nombre.' : null,
            ),
            const SizedBox(height: 12),

            TextFormField(
              controller: _email,
              decoration: const InputDecoration(labelText: 'Correo con el que entra'),
              keyboardType: TextInputType.emailAddress,
              autocorrect: false,
              validator: (v) =>
                  v != null && v.contains('@') ? null : 'Falta el correo.',
            ),
            const SizedBox(height: 12),

            TextFormField(
              controller: _password,
              decoration: const InputDecoration(
                labelText: 'Contraseña',
                helperText: 'Mínimo 8 caracteres. Se la entrega usted.',
              ),
              validator: (v) =>
                  v != null && v.trim().length >= 8 ? null : 'Mínimo 8 caracteres.',
            ),
            const SizedBox(height: 16),

            // Sin sucursal no vería ninguna orden: su bandeja filtra por asignación, pero
            // todo lo demás de la app filtra por sucursal.
            Text(
              'Sucursales donde trabaja',
              style: Theme.of(context).textTheme.labelLarge,
            ),
            for (final branch in widget.branches)
              CheckboxListTile(
                value: _branchIds.contains(branch.id),
                onChanged: (checked) => setState(() {
                  if (checked ?? false) {
                    _branchIds.add(branch.id);
                  } else {
                    _branchIds.remove(branch.id);
                  }
                }),
                contentPadding: EdgeInsets.zero,
                controlAffinity: ListTileControlAffinity.leading,
                dense: true,
                title: Text(branch.name),
              ),

            const SizedBox(height: 12),
            FilledButton(
              onPressed: _branchIds.isEmpty ? null : _submit,
              child: const Text('Crear técnico'),
            ),
          ],
        ),
      ),
    );
  }
}
