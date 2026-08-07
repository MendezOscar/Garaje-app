import 'package:flutter/material.dart';

import '../shared/role_home_scaffold.dart';

class CustomerHomeScreen extends StatelessWidget {
  const CustomerHomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const RoleHomeScaffold(
      title: 'Mis vehículos',
      roleLabel: 'Cliente',
      pendingHint:
          'Fase 1: crear requerimientos y seguir el proceso. Fase 4: ver y aprobar cotizaciones.',
    );
  }
}
