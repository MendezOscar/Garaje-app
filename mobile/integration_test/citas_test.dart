import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/api/appointment_repository.dart';
import 'package:garaj_app/core/api/dashboard_repository.dart';
import 'package:garaj_app/core/api/service_request_repository.dart';
import 'package:garaj_app/core/auth/auth_controller.dart';
import 'package:garaj_app/core/auth/token_store.dart';
import 'package:integration_test/integration_test.dart';

// Las citas, contra la API de verdad: que el taller vea su agenda, que el cliente pueda pedir
// una sobre su vehículo y que al confirmarla deje de pesar en el contador del inicio.
//
// Lo del enlace público —el que abre quien no tiene cuenta— se prueba aparte, en
// backend/tests/smoke/fase16_citas.py: aquí no hay navegador que lo abra.
//
//   flutter test integration_test/citas_test.dart -d <simulador>
//     --dart-define=API_URL=http://localhost:5199
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  Future<ProviderContainer> conSesion(String correo) async {
    await TokenStore().clear();

    final container = ProviderContainer();
    await container.read(authControllerProvider.notifier).login(correo, 'Garaj123!');

    expect(
      container.read(authControllerProvider),
      isA<AuthSignedIn>(),
      reason: 'hace falta la API local en el 5199 con la demostración sembrada',
    );

    return container;
  }

  testWidgets('el taller ve su agenda y la demostración trae citas', (tester) async {
    final container = await conSesion('dueno@tallerdemo.hn');
    addTearDown(container.dispose);

    final citas = await container.read(appointmentRepositoryProvider).list();

    expect(citas, isNotEmpty, reason: 'el sembrador deja tres citas');
    expect(
      citas.any((c) => c.esperaRespuesta),
      isTrue,
      reason: 'alguna tiene que esperar respuesta, que es de lo que va la pantalla',
    );

    // La del enlace público no trae cliente del padrón: es lo que la distingue.
    final delEnlace = citas.where((c) => c.source == AppointmentSource.publicLink);
    expect(delEnlace, isNotEmpty);
    expect(delEnlace.first.vehicleLabel, isNotEmpty);
  });

  testWidgets('el contador del inicio cuenta lo que espera respuesta', (tester) async {
    final container = await conSesion('dueno@tallerdemo.hn');
    addTearDown(container.dispose);

    final resumen = await container.read(dashboardRepositoryProvider).get();
    final citas = await container.read(appointmentRepositoryProvider).list();

    expect(
      resumen.appointmentsToConfirm,
      citas.where((c) => c.esperaRespuesta).length,
      reason: 'el inicio y la agenda tienen que contar lo mismo',
    );
  });

  testWidgets('el cliente pide una cita sobre su vehículo', (tester) async {
    final container = await conSesion('cliente1@tallerdemo.hn');
    addTearDown(container.dispose);

    // El Cliente no tiene sucursales asignadas —no trabaja en ninguna—, así que elige entre
    // las del taller, igual que en la pantalla de pedir cita.
    final sucursales = await container.read(serviceRequestRepositoryProvider).branches();
    expect(sucursales, isNotEmpty, reason: 'el taller de la demostración tiene sucursales');

    final repo = container.read(appointmentRepositoryProvider);
    final antes = await repo.list();

    final manana = DateTime.now().add(const Duration(days: 1));

    // El cliente elige entre los suyos: la API le devuelve solo esos.
    final cita = await repo.request(
      branchId: sucursales.first.id,
      vehicleId: await _unVehiculoSuyo(container),
      reason: 'Prueba de integración: servicio',
      day: manana,
      slot: AppointmentSlot.afternoon,
    );

    expect(cita.status, AppointmentStatus.requested);
    expect(cita.source, AppointmentSource.customerApp);
    expect(cita.vehicleId, isNotNull, reason: 'la de la app sí trae su vehículo');

    final despues = await repo.list();
    expect(despues.length, antes.length + 1);
  });
}

/// Un vehículo del cliente de la sesión. La API ya le devuelve solo los suyos.
Future<String> _unVehiculoSuyo(ProviderContainer container) async {
  final dio = container.read(apiClientProvider).dio;
  final response = await dio.get<Map<String, dynamic>>(
    '/api/vehicles',
    queryParameters: {'pageSize': 1},
  );

  final items = response.data!['items'] as List<dynamic>;
  expect(items, isNotEmpty, reason: 'el cliente de la demostración tiene vehículos');

  return (items.first as Map<String, dynamic>)['id'] as String;
}
