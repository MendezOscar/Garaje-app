import 'package:flutter/material.dart';

import '../home/today_screen.dart';
import '../reports/cash_close_screen.dart';
import '../work_orders/work_order_list_screen.dart';
import 'more_screen.dart';

/// El armazón del Dueño: cuatro destinos en la barra de abajo.
///
/// Antes la barra de abajo —el mejor sitio de la pantalla, el que alcanza el pulgar— la gastaba
/// una línea de texto con el nombre del usuario, y todo el taller vivía detrás de un menú «⋯»
/// en la esquina de arriba. Aquí hace lo que se toca: Hoy, Órdenes, Caja y Más.
///
/// Solo el Dueño. El Técnico y el Cliente entran directo a su bandeja: la de ellos está por
/// diseñar, y darles una barra con destinos que su perfil no puede abrir sería peor que nada.
class OwnerShell extends StatefulWidget {
  const OwnerShell({super.key});

  @override
  State<OwnerShell> createState() => _OwnerShellState();
}

class _OwnerShellState extends State<OwnerShell> {
  int _index = 0;

  /// Las pestañas se arman al visitarlas por primera vez y de ahí en adelante se conservan.
  /// Armarlas todas de una vez costaría cuatro peticiones al abrir la app, y en el taller la
  /// señal es la que es.
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
      TodayScreen(onVerOrdenes: () => _ir(1)),
      const WorkOrderListScreen(
        title: 'Órdenes',
        emptyMessage: 'No hay órdenes abiertas en el taller.',
      ),
      const CashCloseScreen(),
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
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home),
            label: 'Hoy',
          ),
          NavigationDestination(
            icon: Icon(Icons.list_alt_outlined),
            selectedIcon: Icon(Icons.list_alt),
            label: 'Órdenes',
          ),
          NavigationDestination(
            icon: Icon(Icons.point_of_sale_outlined),
            selectedIcon: Icon(Icons.point_of_sale),
            label: 'Caja',
          ),
          NavigationDestination(
            icon: Icon(Icons.more_horiz),
            label: 'Más',
          ),
        ],
      ),
    );
  }
}
