import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Qué sucursal se está mirando. `null` es «todas».
///
/// Es una sola para toda la aplicación —el inicio, la caja y la bandeja de órdenes miran la
/// misma—, igual que el selector de la barra de arriba del panel web: con una por pantalla,
/// las cifras de Hoy y la lista de Órdenes podrían estar contando talleres distintos sin que
/// nada lo dijera.
///
/// Vive en memoria a propósito: al volver a abrir la aplicación se arranca viendo el taller
/// completo, que es lo que se quiere la mayoría de las veces.
final sucursalActivaProvider =
    NotifierProvider<SucursalActiva, String?>(SucursalActiva.new);

class SucursalActiva extends Notifier<String?> {
  @override
  String? build() => null;

  void set(String? value) => state = value;
}
