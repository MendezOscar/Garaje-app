import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../api/app_version_repository.dart';

/// El portón de versión, encima de toda la app.
///
/// Con la app al día no se ve nada. Cuando hay una más nueva, una franja arriba que se puede
/// cerrar; cuando la que está instalada ya no le sirve a la API, una pantalla que no deja
/// pasar y manda a la tienda.
///
/// Va en el `builder` de la aplicación y no dentro de una pantalla: el bloqueo tiene que
/// alcanzar también a quien dejó la app abierta en la orden que estaba trabajando. Ahí dentro
/// ya hay tema, dirección de texto y `MediaQuery`, que es lo que necesitan la franja y la
/// pantalla de bloqueo para verse como el resto de la app.
class PortonDeVersion extends ConsumerStatefulWidget {
  const PortonDeVersion({required this.child, super.key});

  final Widget child;

  @override
  ConsumerState<PortonDeVersion> createState() => _PortonDeVersionState();
}

class _PortonDeVersionState extends ConsumerState<PortonDeVersion> {
  /// El aviso suave se cierra y no vuelve hasta la próxima vez que se abra la app. Insistir
  /// en cada pantalla sería castigar a quien ya decidió actualizar después.
  bool _avisoCerrado = false;

  @override
  Widget build(BuildContext context) {
    final version = ref.watch(versionDeLaAppProvider).value ?? VersionDeLaApp.desconocida;

    if (version.bloquea) return _Bloqueo(version: version);

    if (!version.avisa || _avisoCerrado) return widget.child;

    return Column(
      children: [
        _Aviso(
          version: version,
          onCerrar: () => setState(() => _avisoCerrado = true),
        ),
        Expanded(child: widget.child),
      ],
    );
  }
}

/// La franja de arriba: hay una versión más nueva, pero se puede seguir trabajando.
class _Aviso extends StatelessWidget {
  const _Aviso({required this.version, required this.onCerrar});

  final VersionDeLaApp version;
  final VoidCallback onCerrar;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Material(
      color: theme.colorScheme.secondaryContainer,
      child: SafeArea(
        bottom: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 8, 8),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  version.mensaje ?? 'Hay una versión más nueva de la app.',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.onSecondaryContainer,
                  ),
                ),
              ),
              TextButton(
                onPressed: () => _abrirTienda(version.storeUrl),
                child: const Text('Actualizar'),
              ),
              // Sin tooltip ni nada que necesite un `Overlay`: esta franja va en el `builder`
              // de la aplicación, que está por encima del Navigator, y un Tooltip ahí arriba
              // revienta por no encontrar el overlay.
              TextButton(onPressed: onCerrar, child: const Text('Ahora no')),
            ],
          ),
        ),
      ),
    );
  }
}

/// La pantalla que no deja pasar.
class _Bloqueo extends StatelessWidget {
  const _Bloqueo({required this.version});

  final VersionDeLaApp version;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  Icons.system_update,
                  size: 56,
                  color: theme.colorScheme.primary,
                ),
                const SizedBox(height: 20),
                Text(
                  'Hay que actualizar la app',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.titleLarge,
                ),
                const SizedBox(height: 10),
                Text(
                  version.mensaje ??
                      'Esta versión ya no funciona con el sistema. Actualícela desde la '
                          'tienda y vuelva a entrar; su trabajo no se pierde.',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 24),
                FilledButton.icon(
                  onPressed: () => _abrirTienda(version.storeUrl),
                  icon: const Icon(Icons.open_in_new),
                  label: const Text('Abrir la tienda'),
                ),
                const SizedBox(height: 20),
                // Qué versión tiene y cuál hace falta: es lo que se pregunta por teléfono
                // cuando alguien llama diciendo que la app no lo deja entrar.
                Text(
                  'Instalada ${version.compilacionInstalada} · '
                  'hace falta ${version.compilacionMinima}',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

Future<void> _abrirTienda(String url) async {
  if (url.isEmpty) return;
  await launchUrl(Uri.parse(url), mode: LaunchMode.externalApplication);
}
