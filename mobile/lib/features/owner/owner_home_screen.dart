import 'package:flutter/material.dart';

import '../shared/role_home_scaffold.dart';

class OwnerHomeScreen extends StatelessWidget {
  const OwnerHomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const RoleHomeScaffold(
      title: 'Taller',
      roleLabel: 'Dueño',
      pendingHint:
          'Fase 1: bandeja de requerimientos, asignación de técnicos y órdenes en curso.',
    );
  }
}
