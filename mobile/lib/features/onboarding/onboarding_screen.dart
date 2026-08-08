import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/onboarding/onboarding_controller.dart';
import '../../core/theme/garaj_brand.dart';
import '../shared/brand_logo.dart';

/// Bienvenida de la primera instalación. Tres pantallas y se acabó.
///
/// No pide datos ni permisos: los permisos se piden cuando hacen falta —la cámara al tomar
/// la primera foto—, que es cuando se entiende para qué son. Un recorrido que arranca
/// pidiendo cosas se salta entero.
class OnboardingScreen extends ConsumerStatefulWidget {
  const OnboardingScreen({super.key});

  @override
  ConsumerState<OnboardingScreen> createState() => _OnboardingScreenState();
}

class _OnboardingScreenState extends ConsumerState<OnboardingScreen> {
  final _controller = PageController();
  int _page = 0;

  static const _pages = [
    _Slide(
      icon: Icons.checklist_rounded,
      title: 'El trabajo, paso a paso',
      body: 'Cada vehículo con su motivo de ingreso y el avance de la reparación, '
          'de recibido a entregado. Nadie tiene que llamar para preguntar cómo va.',
    ),
    _Slide(
      icon: Icons.photo_camera_outlined,
      title: 'Con fotos de todo',
      body: 'El técnico documenta lo que encuentra y lo que cambia, incluso sin señal: '
          'las fotos se suben solas al recuperar la red.',
    ),
    _Slide(
      icon: Icons.receipt_long_outlined,
      title: 'Cotizar y cobrar',
      body: 'Cotizaciones que salen por WhatsApp y se aprueban con un toque. '
          'Al entregar, la orden se factura y el ingreso aparece en el reporte del día.',
    ),
  ];

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  bool get _isLast => _page == _pages.length - 1;

  Future<void> _finish() => ref.read(onboardingProvider.notifier).complete();

  void _next() {
    if (_isLast) {
      _finish();
      return;
    }

    _controller.nextPage(
      duration: const Duration(milliseconds: 260),
      curve: Curves.easeOut,
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: GarajColors.brand,
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 12, 12, 0),
              child: Row(
                children: [
                  const BrandMark(size: 30, inverted: true),
                  const SizedBox(width: 10),
                  const Text(
                    'GarajApp',
                    style: TextStyle(
                      fontFamily: GarajFonts.display,
                      fontWeight: FontWeight.w700,
                      fontSize: 20,
                      color: Colors.white,
                    ),
                  ),
                  const Spacer(),
                  // Siempre visible, incluso en la última: quien ya sabe lo que hace la
                  // aplicación no tiene por qué pasar tres pantallas para entrar.
                  TextButton(
                    onPressed: _finish,
                    style: TextButton.styleFrom(foregroundColor: Colors.white70),
                    child: const Text('Saltar'),
                  ),
                ],
              ),
            ),

            Expanded(
              child: PageView.builder(
                controller: _controller,
                itemCount: _pages.length,
                onPageChanged: (i) => setState(() => _page = i),
                itemBuilder: (_, i) => _pages[i],
              ),
            ),

            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                for (var i = 0; i < _pages.length; i++)
                  AnimatedContainer(
                    duration: const Duration(milliseconds: 200),
                    margin: const EdgeInsets.symmetric(horizontal: 4),
                    width: i == _page ? 22 : 8,
                    height: 8,
                    decoration: BoxDecoration(
                      color: i == _page ? Colors.white : Colors.white38,
                      borderRadius: BorderRadius.circular(4),
                    ),
                  ),
              ],
            ),

            Padding(
              padding: const EdgeInsets.fromLTRB(24, 24, 24, 24),
              child: FilledButton(
                onPressed: _next,
                style: FilledButton.styleFrom(
                  backgroundColor: Colors.white,
                  foregroundColor: GarajColors.brand,
                ),
                child: Text(_isLast ? 'Entrar' : 'Siguiente'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Slide extends StatelessWidget {
  const _Slide({required this.icon, required this.title, required this.body});

  final IconData icon;
  final String title;
  final String body;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 32),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 88,
            height: 88,
            decoration: BoxDecoration(
              color: Colors.white24,
              borderRadius: BorderRadius.circular(22),
            ),
            child: Icon(icon, size: 44, color: Colors.white),
          ),
          const SizedBox(height: 32),
          Text(
            title,
            style: const TextStyle(
              fontFamily: GarajFonts.display,
              fontWeight: FontWeight.w700,
              fontSize: 28,
              height: 1.15,
              letterSpacing: -0.8,
              color: Colors.white,
            ),
          ),
          const SizedBox(height: 14),
          Text(
            body,
            style: const TextStyle(fontSize: 16, height: 1.5, color: Colors.white),
          ),
        ],
      ),
    );
  }
}
