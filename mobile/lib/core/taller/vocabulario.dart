import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/current_user.dart';

/// Con qué trabaja el taller y cómo se llaman las cosas en pantalla.
///
/// Un taller mecánico recibe vehículos con placa y kilometraje; uno de electrónica recibe
/// equipos con número de serie. Es la misma pantalla con otras palabras, así que las palabras
/// salen de aquí y no se escriben a mano en cada vista.
///
/// Sale de la sesión y no de la ficha del taller: la ficha solo la puede leer el Dueño, y
/// estas palabras las necesitan todas las pantallas, también las del técnico y las del
/// cliente. Además ya viene con el login, así que no es un viaje de red más.
final esDeEquiposProvider = Provider<bool>((ref) {
  final sesion = ref.watch(authControllerProvider);
  return sesion is AuthSignedIn && sesion.user.worksWith == WorkshopKind.equipos;
});

class Vocabulario {
  const Vocabulario({
    required this.activo,
    required this.activoMayus,
    required this.activos,
    required this.activosMayus,
    required this.identificador,
    required this.recibir,
  });

  /// "vehículo" o "equipo".
  final String activo;
  final String activoMayus;
  final String activos;
  final String activosMayus;

  /// "Placa" o "Serie".
  final String identificador;

  /// "Recibir vehículo" o "Recibir equipo".
  final String recibir;
}

const _deVehiculos = Vocabulario(
  activo: 'vehículo',
  activoMayus: 'Vehículo',
  activos: 'vehículos',
  activosMayus: 'Vehículos',
  identificador: 'Placa',
  recibir: 'Recibir vehículo',
);

const _deEquipos = Vocabulario(
  activo: 'equipo',
  activoMayus: 'Equipo',
  activos: 'equipos',
  activosMayus: 'Equipos',
  identificador: 'Serie',
  recibir: 'Recibir equipo',
);

final vocabularioProvider = Provider<Vocabulario>(
  (ref) => ref.watch(esDeEquiposProvider) ? _deEquipos : _deVehiculos,
);
