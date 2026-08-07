import 'package:flutter/material.dart';

import '../shared/role_home_scaffold.dart';

class TechnicianHomeScreen extends StatelessWidget {
  const TechnicianHomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const RoleHomeScaffold(
      title: 'Mis asignaciones',
      roleLabel: 'Técnico',
      pendingHint:
          'Fase 1: órdenes asignadas, checklist de pasos y cambios de estado. '
          'Fase 2: cámara y cola de subida offline.',
    );
  }
}
