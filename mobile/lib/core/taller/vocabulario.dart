import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/tenant_repository.dart';

/// Con qué trabaja el taller y cómo se llaman las cosas en pantalla.
///
/// Un taller mecánico recibe vehículos con placa y kilometraje; uno de electrónica recibe
/// equipos con número de serie. Es la misma pantalla con otras palabras, así que las palabras
/// salen de aquí y no se escriben a mano en cada vista.
///
/// Vive fuera de `tenantSettingsProvider` —que se descarta al salir de Ajustes— porque esto
/// lo lee media aplicación y pedirlo en cada pantalla sería un viaje de red por pantalla.
final tallerProvider = FutureProvider<TenantSettings>(
  (ref) => ref.watch(tenantRepositoryProvider).settings(),
);

/// Falso mientras se carga: lo de hoy es un taller de vehículos, así que es lo que se asume
/// hasta que el servidor conteste otra cosa.
final esDeEquiposProvider = Provider<bool>(
  (ref) => ref.watch(tallerProvider).value?.worksWith == WorkshopKind.equipos,
);

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
