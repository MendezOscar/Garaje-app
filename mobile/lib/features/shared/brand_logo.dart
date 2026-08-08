import 'package:flutter/material.dart';

import '../../core/theme/garaj_brand.dart';

/// La marca dibujada, no una imagen.
///
/// La tuerca es un hexágono con el centro vacío y la G va en Space Grotesk, que ya está
/// empaquetada. Dibujarla sale más barato que arrastrar `flutter_svg` —y sobre todo escala
/// sin borrarse en cualquier densidad de pantalla.
class BrandMark extends StatelessWidget {
  const BrandMark({this.size = 72, this.inverted = false, super.key});

  final double size;

  /// Sobre azul o grafito la marca va en blanco pleno, como pide la guía.
  final bool inverted;

  @override
  Widget build(BuildContext context) {
    final dark = Theme.of(context).brightness == Brightness.dark;

    return SizedBox(
      // Siempre cuadrada: la tuerca nunca se estira para llenar un hueco.
      width: size,
      height: size,
      child: CustomPaint(
        painter: _NutPainter(
          nut: inverted
              ? Colors.white
              : (dark ? GarajColors.brandLight : GarajColors.brand),
        ),
        child: Center(
          child: Text(
            'G',
            style: TextStyle(
              fontFamily: GarajFonts.display,
              fontWeight: FontWeight.w700,
              fontSize: size * 0.40,
              letterSpacing: -size * 0.016,
              height: 1,
              color: inverted
                  ? Colors.white
                  : (dark ? Colors.white : GarajColors.ink),
            ),
          ),
        ),
      ),
    );
  }
}

class _NutPainter extends CustomPainter {
  const _NutPainter({required this.nut});

  final Color nut;

  @override
  void paint(Canvas canvas, Size size) {
    // Coordenadas del paquete de marca, sobre una caja de 100×100, reescaladas.
    double x(double v) => v / 100 * size.width;
    double y(double v) => v / 100 * size.height;

    Path hexagon(List<List<double>> points) {
      final path = Path()..moveTo(x(points.first[0]), y(points.first[1]));
      for (final p in points.skip(1)) {
        path.lineTo(x(p[0]), y(p[1]));
      }
      return path..close();
    }

    final outer = hexagon([
      [50, 0], [93, 25], [93, 75], [50, 100], [7, 75], [7, 25],
    ]);

    final inner = hexagon([
      [50, 17.75], [77.7, 33.88], [77.7, 66.12],
      [50, 82.25], [22.3, 66.12], [22.3, 33.88],
    ]);

    // La diferencia deja el aro de la tuerca: el centro queda hueco y por él se ve la G.
    canvas.drawPath(
      Path.combine(PathOperation.difference, outer, inner),
      Paint()..color = nut,
    );
  }

  @override
  bool shouldRepaint(_NutPainter old) => old.nut != nut;
}

/// Marca completa con la palabra debajo. Para pantallas de entrada y bienvenida.
class BrandLockup extends StatelessWidget {
  const BrandLockup({this.markSize = 84, this.inverted = false, super.key});

  final double markSize;
  final bool inverted;

  @override
  Widget build(BuildContext context) {
    final dark = Theme.of(context).brightness == Brightness.dark;

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        BrandMark(size: markSize, inverted: inverted),
        SizedBox(height: markSize * 0.18),
        Text(
          'GarajApp',
          style: TextStyle(
            fontFamily: GarajFonts.display,
            fontWeight: FontWeight.w700,
            fontSize: markSize * 0.34,
            letterSpacing: -markSize * 0.012,
            color: inverted
                ? Colors.white
                : (dark ? Colors.white : GarajColors.ink),
          ),
        ),
      ],
    );
  }
}
