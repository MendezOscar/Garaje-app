import 'package:flutter/material.dart';

import '../inventory/inventory_screen.dart';
import '../technician/my_work_screen.dart';
import 'more_screen.dart';

/// El armazón del Técnico: tres destinos abajo.
///
/// Los repuestos salen del menú «⋯» y ganan su lugar porque los consulta a diario —antes de
/// prometer una reparación tiene que saber si hay existencia—, y el resto cabe en Más.
class TechnicianShell extends StatefulWidget {
  const TechnicianShell({super.key});

  @override
  State<TechnicianShell> createState() => _TechnicianShellState();
}

class _TechnicianShellState extends State<TechnicianShell> {
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
      const MyWorkScreen(),
      const InventoryScreen(),
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
            icon: Icon(Icons.build_outlined),
            selectedIcon: Icon(Icons.build),
            label: 'Mi trabajo',
          ),
          NavigationDestination(
            icon: Icon(Icons.inventory_2_outlined),
            selectedIcon: Icon(Icons.inventory_2),
            label: 'Repuestos',
          ),
          NavigationDestination(icon: Icon(Icons.more_horiz), label: 'Más'),
        ],
      ),
    );
  }
}
