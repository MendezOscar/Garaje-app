import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../customer/my_vehicles_screen.dart';
import '../customer/vehicle_history_screen.dart';
import 'more_screen.dart';

/// El armazón del Cliente: su vehículo, su historial y nada más.
///
/// Tres destinos y ningún folio a la vista. Lo que antes era una bandeja de órdenes con
/// buscador y filtro —la misma del taller— aquí es una pantalla por pregunta.
class CustomerShell extends ConsumerStatefulWidget {
  const CustomerShell({super.key});

  @override
  ConsumerState<CustomerShell> createState() => _CustomerShellState();
}

class _CustomerShellState extends ConsumerState<CustomerShell> {
  int _index = 0;
  final _visitadas = <int>{0};

  void _ir(int index) {
    setState(() {
      _index = index;
      _visitadas.add(index);
    });
  }

  @override
  Widget build(BuildContext context) {
    final pestanas = <Widget>[
      MyVehiclesScreen(
        onVerHistorial: (vehicleId) {
          ref.read(vehiculoElegidoProvider.notifier).set(vehicleId);
          _ir(1);
        },
      ),
      const VehicleHistoryScreen(),
      const MoreScreen(),
    ];

    return Scaffold(
      body: IndexedStack(
        index: _index,
        children: [
          for (var i = 0; i < pestanas.length; i++)
            _visitadas.contains(i) ? pestanas[i] : const SizedBox.shrink(),
        ],
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: _ir,
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.directions_car_outlined),
            selectedIcon: Icon(Icons.directions_car),
            label: 'Mi vehículo',
          ),
          NavigationDestination(
            icon: Icon(Icons.history_outlined),
            selectedIcon: Icon(Icons.history),
            label: 'Historial',
          ),
          NavigationDestination(icon: Icon(Icons.more_horiz), label: 'Más'),
        ],
      ),
    );
  }
}
