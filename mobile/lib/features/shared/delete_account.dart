import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/models/current_user.dart';

/// Borra la cuenta de quien está dentro, tras confirmarlo.
///
/// Un solo diálogo y dos toques: Apple exige que borrar la cuenta se pueda hacer desde la app y
/// sin trámites, así que nada de escribir palabras ni de mandar correos. Lo que sí cambia es la
/// advertencia, porque para el Dueño la consecuencia no es la misma.
///
/// Vive aquí y no en una pantalla porque la piden dos: el Dueño desde «Más» y los otros dos
/// perfiles desde el menú de su bandeja.
Future<void> confirmarEliminarCuenta(
  BuildContext context,
  WidgetRef ref,
  AppRole role,
) async {
  final aviso = switch (role) {
    AppRole.owner =>
      'Perderá el acceso al taller. Si es el único Dueño, nadie más podrá entrar a administrarlo '
          'y habrá que comunicarse con GarajApp para recuperarlo.',
    AppRole.technician =>
      'Perderá el acceso. El trabajo que ya registró se queda en el taller, sin su nombre.',
    AppRole.customer =>
      'Perderá el acceso a la app. Su taller conserva sus vehículos y su historial de '
          'reparaciones, y puede volver a darle acceso cuando quiera.',
  };

  final confirmado = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: const Text('¿Eliminar su cuenta?'),
      content: Text(
        '$aviso\n\nSe borran su nombre, su correo y su contraseña, y no se puede deshacer.',
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context, false),
          child: const Text('Cancelar'),
        ),
        TextButton(
          onPressed: () => Navigator.pop(context, true),
          style: TextButton.styleFrom(
            foregroundColor: Theme.of(context).colorScheme.error,
          ),
          child: const Text('Eliminar'),
        ),
      ],
    ),
  );

  if (confirmado != true || !context.mounted) return;

  try {
    await ref.read(authControllerProvider.notifier).deleteAccount();
  } catch (e) {
    if (!context.mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(apiErrorMessage(e, 'No se pudo eliminar la cuenta.'))),
    );
  }
}
