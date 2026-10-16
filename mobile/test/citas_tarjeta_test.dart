import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/api/appointment_repository.dart';
import 'package:garaj_app/core/theme/garaj_brand.dart';
import 'package:garaj_app/features/appointments/appointments_screen.dart';

/// La tarjeta de una cita, en el teléfono más angosto que se usa y con la letra grande.
///
/// Se rompió así: los botones iban en una fila con un espaciador, y en cuanto no cabían, el
/// de la derecha —«Confirmar», justo el que hay que tocar— quedaba cortado contra el borde.
/// Desde fuera no se ve un error: se ve que el botón no está.
void main() {
  AppointmentItem cita(AppointmentStatus estado) => AppointmentItem(
        id: 'una',
        branchId: 'sucursal',
        branchName: 'Sucursal Centro',
        contactName: 'Marvin Zelaya',
        contactPhone: '50499112233',
        vehicleLabel: 'Hilux 2016, placa HAE4455',
        reason: 'Hace un ruido al frenar y quiere saber cuánto cuesta',
        day: DateTime.now().add(const Duration(days: 1)),
        slot: AppointmentSlot.morning,
        status: estado,
        source: AppointmentSource.publicLink,
      );

  Future<void> montar(
    WidgetTester tester,
    AppointmentStatus estado, {
    double letra = 1.0,
    double ancho = 390,
  }) async {
    tester.view.physicalSize = Size(ancho, 844);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      MaterialApp(
        theme: garajTheme,
        home: MediaQuery(
          data: MediaQueryData(textScaler: TextScaler.linear(letra)),
          child: Scaffold(
            body: ListView(
              padding: const EdgeInsets.all(12),
              children: [
                CitaCard(
                  cita: cita(estado),
                  busy: false,
                  onConfirmar: () {},
                  onRechazar: () {},
                  onLlego: () {},
                  onWhatsapp: () {},
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  testWidgets('la cita por confirmar enseña sus dos botones', (tester) async {
    await montar(tester, AppointmentStatus.requested);

    expect(find.text('Confirmar'), findsOneWidget);
    expect(find.text('Rechazar'), findsOneWidget);
    expect(tester.takeException(), isNull, reason: 'la fila no debe salirse de la pantalla');

    // Que esté pintado no basta: tiene que poder tocarse.
    await tester.ensureVisible(find.text('Confirmar'));
    await tester.pump();
    expect(find.text('Confirmar').hitTestable(), findsOneWidget);
  });

  testWidgets('y los sigue enseñando con la letra grande del sistema', (tester) async {
    await montar(tester, AppointmentStatus.requested, letra: 1.6);

    // Con la letra grande la tarjeta crece y el botón puede quedar bajo el pliegue: eso se
    // resuelve rodando la lista. Lo que no se resuelve rodando es que se salga de lado.
    await tester.ensureVisible(find.text('Confirmar'));
    await tester.pump();

    expect(find.text('Confirmar').hitTestable(), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('en un teléfono angosto tampoco se corta', (tester) async {
    // 320 es el iPhone SE de primera generación: el suelo de lo que hay en un taller.
    await montar(tester, AppointmentStatus.requested, ancho: 320);

    await tester.ensureVisible(find.text('Confirmar'));
    await tester.pump();

    expect(find.text('Confirmar').hitTestable(), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('la confirmada ofrece cerrar y mover el día', (tester) async {
    await montar(tester, AppointmentStatus.confirmed);

    await tester.ensureVisible(find.text('Llegó'));
    await tester.pump();

    expect(find.text('Llegó').hitTestable(), findsOneWidget);
    expect(find.text('Cambiar día').hitTestable(), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
