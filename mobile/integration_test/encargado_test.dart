import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garaj_app/core/api/work_order_repository.dart';
import 'package:garaj_app/core/auth/auth_controller.dart';
import 'package:garaj_app/core/auth/token_store.dart';
import 'package:garaj_app/core/models/current_user.dart';
import 'package:garaj_app/core/theme/garaj_brand.dart';
import 'package:garaj_app/features/shell/more_screen.dart';
import 'package:integration_test/integration_test.dart';

// El Encargado de sucursal, contra la API de verdad: que vea lo suyo y que no le aparezca en
// el menú lo que la API le va a negar. Las puertas cerradas —la sucursal ajena, los catálogos,
// el personal— se prueban aparte, en backend/tests/smoke/fase15_encargado.py.
//
//   flutter test integration_test/encargado_test.dart -d <simulador>
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

  Future<void> montar(WidgetTester tester, ProviderContainer container, Widget pantalla) async {
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp(theme: garajTheme, home: pantalla),
      ),
    );

    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
  }

  testWidgets('el encargado entra con su perfil y su sucursal', (tester) async {
    final container = await conSesion('encargado@tallerdemo.hn');
    addTearDown(container.dispose);

    final usuario = (container.read(authControllerProvider) as AuthSignedIn).user;

    expect(usuario.role, AppRole.manager);
    expect(usuario.manda, isTrue);
    expect(usuario.esDueno, isFalse);

    // Las sucursales del token son las suyas: con una sola, el selector ni se pinta.
    expect(usuario.branches, hasLength(1));
  });

  testWidgets('el menú del encargado no ofrece lo que es del Dueño', (tester) async {
    final container = await conSesion('encargado@tallerdemo.hn');
    addTearDown(container.dispose);

    await montar(tester, container, const MoreScreen());

    expect(find.text('Encargado · Taller Demo'), findsOneWidget);

    // Lo suyo: el día a día del negocio en su sucursal.
    for (final renglon in ['Requerimientos', 'Reclamos', 'Por cobrar', 'Ventas', 'Reportes']) {
      expect(find.text(renglon), findsOneWidget, reason: '$renglon es del Encargado también');
    }

    // Lo del taller entero: si apareciera, el toque abriría una pantalla que solo puede
    // fallar con 403.
    for (final renglon in [
      'Mano de obra',
      'Trabajos frecuentes',
      'Resultados y gastos',
      'Usuarios',
      'Taller',
    ]) {
      expect(find.text(renglon), findsNothing, reason: '$renglon es solo del Dueño');
    }
  });

  testWidgets('la bandeja del encargado trae solo las órdenes de su sucursal', (tester) async {
    final suyas = await (await conSesion('encargado@tallerdemo.hn'))
        .read(workOrderRepositoryProvider)
        .list(onlyOpen: false);

    final delDueno = await (await conSesion('dueno@tallerdemo.hn'))
        .read(workOrderRepositoryProvider)
        .list(onlyOpen: false);

    expect(suyas, isNotEmpty);
    expect(suyas.length, lessThan(delDueno.length));

    final sucursales = suyas.map((o) => o.branchName).toSet();
    expect(sucursales, hasLength(1), reason: 'se le coló una orden de otra sucursal');
  });
}
