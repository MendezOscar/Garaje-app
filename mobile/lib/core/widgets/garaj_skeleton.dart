import 'package:flutter/material.dart';

import '../theme/garaj_brand.dart';

/// Una caja gris que late mientras llegan los datos.
///
/// Es el ladrillo: una línea de texto, un avatar, un botón. Para una lista entera está
/// [GarajSkeletonList], que es lo que se usa casi siempre.
class GarajSkeleton extends StatefulWidget {
  const GarajSkeleton({super.key, this.width, this.height = 12, this.radius = 6});

  final double? width;
  final double height;
  final double radius;

  @override
  State<GarajSkeleton> createState() => _GarajSkeletonState();
}

class _GarajSkeletonState extends State<GarajSkeleton> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1400),
  )..repeat();

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final oscuro = Theme.of(context).brightness == Brightness.dark;
    final base = oscuro ? GarajColors.surfaceAltDark : GarajColors.surfaceAlt;
    final brillo = Color.alphaBlend(
      (oscuro ? Colors.white : Colors.black).withValues(alpha: 0.04),
      base,
    );

    // Quien pidió menos movimiento en el teléfono no tiene por qué ver esto parpadeando.
    final quieto = MediaQuery.maybeDisableAnimationsOf(context) ?? false;

    final caja = DecoratedBox(
      decoration: BoxDecoration(
        color: base,
        borderRadius: BorderRadius.circular(widget.radius),
      ),
      child: SizedBox(width: widget.width, height: widget.height),
    );

    if (quieto) return caja;

    return AnimatedBuilder(
      animation: _c,
      builder: (context, _) {
        final x = _c.value * 2 - 1;
        return ShaderMask(
          blendMode: BlendMode.srcATop,
          shaderCallback: (rect) => LinearGradient(
            begin: Alignment(x - 1, 0),
            end: Alignment(x + 1, 0),
            colors: [base, brillo, base],
          ).createShader(rect),
          child: caja,
        );
      },
    );
  }
}

/// El hueco de la lista que viene.
///
/// Sustituye a la ruedita en medio de la pantalla: así se ve de una vez cuántas filas van a
/// aparecer y dónde, y cuando llegan los datos nada salta de sitio. Va dentro de un
/// `RefreshIndicator` igual que la lista de verdad, por eso es un `ListView`.
class GarajSkeletonList extends StatelessWidget {
  const GarajSkeletonList({super.key, this.rows = 5, this.card = true});

  /// Cuántas filas dibujar. Cinco llenan una pantalla de teléfono sin pasarse.
  final int rows;

  /// `true` para filas con borde —listas de tarjetas—, `false` para renglones sueltos.
  final bool card;

  @override
  Widget build(BuildContext context) {
    final border = Theme.of(context).dividerColor;

    return Semantics(
      label: 'Cargando',
      liveRegion: true,
      child: ListView.separated(
        padding: const EdgeInsets.all(GarajSpace.md),
        itemCount: rows,
        separatorBuilder: (_, _) => const SizedBox(height: GarajSpace.sm),
        itemBuilder: (context, i) => Container(
          padding: const EdgeInsets.all(GarajSpace.md),
          decoration: card
              ? BoxDecoration(
                  border: Border.all(color: border),
                  borderRadius: BorderRadius.circular(10),
                )
              : null,
          child: const Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              GarajSkeleton(width: 180, height: 14),
              SizedBox(height: GarajSpace.sm),
              GarajSkeleton(width: 110),
            ],
          ),
        ),
      ),
    );
  }
}
