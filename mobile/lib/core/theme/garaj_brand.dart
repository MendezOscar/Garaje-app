import 'package:flutter/material.dart';

/// Colores de marca de GarajApp.
///
/// Cualquier color de la aplicación sale de aquí. Si una pantalla escribe un `Color(0xFF…)`
/// suelto, el día que la marca cambie ese punto se queda atrás y nadie lo nota hasta verlo
/// en producción.
class GarajColors {
  const GarajColors._();

  static const brand = Color(0xFF1F6FEB);
  static const brandDeep = Color(0xFF124293);
  static const ink = Color(0xFF14171C);

  static const bg = Color(0xFFF7F8FA);
  static const surface = Color(0xFFFFFFFF);
  static const surfaceAlt = Color(0xFFEEF0F4);
  static const border = Color(0xFFD9DDE4);
  // 4,88:1 sobre el fondo claro y 4,54:1 sobre `surfaceAlt`. El #6B7480 anterior daba 4,46:1,
  // por debajo del 4,5:1 que pide la AA para texto de 15 px.
  static const textMuted = Color(0xFF646E7A);

  // Colores de estado, y solo de estado: nunca como fondo decorativo. Si todo lleva color,
  // el color deja de avisar de nada.
  static const warning = Color(0xFFF2A31A); // espera aprobación / espera repuestos
  static const success = Color(0xFF1FA971); // listo, entregado, pagado
  static const danger = Color(0xFFC0392B); // cancelado, sin stock

  // Variantes para fondo oscuro: los tonos claros no alcanzan el contraste mínimo sobre
  // grafito, así que suben de luminosidad.
  static const bgDark = Color(0xFF14161A);
  static const surfaceDark = Color(0xFF1C1F26);
  static const surfaceAltDark = Color(0xFF262A33);
  static const borderDark = Color(0xFF333944);
  static const brandLight = Color(0xFF5C9DFF);
  static const warningLight = Color(0xFFFFC14D);
  static const successLight = Color(0xFF46C892);
  static const dangerLight = Color(0xFFFF6B5B);
}

class GarajFonts {
  const GarajFonts._();

  /// Marca y titulares.
  static const display = 'SpaceGrotesk';

  /// Interfaz y texto corrido.
  static const sans = 'IBMPlexSans';

  /// Folios, cantidades y montos: cifras que alguien va a comparar con la de la fila de abajo.
  static const mono = 'IBMPlexMono';
}

/// Estilo para cifras y correlativos (`MTZ-000123`, `L 2,150.00`).
const monoStyle = TextStyle(
  fontFamily: GarajFonts.mono,
  fontFeatures: [FontFeature.tabularFigures()],
);

ThemeData _theme(Brightness brightness) {
  final dark = brightness == Brightness.dark;

  final scheme = ColorScheme.fromSeed(
    seedColor: GarajColors.brand,
    brightness: brightness,
    primary: dark ? GarajColors.brandLight : GarajColors.brand,
    error: dark ? GarajColors.dangerLight : GarajColors.danger,
    surface: dark ? GarajColors.surfaceDark : GarajColors.surface,
  );

  final border = dark ? GarajColors.borderDark : GarajColors.border;

  return ThemeData(
    useMaterial3: true,
    brightness: brightness,
    colorScheme: scheme,
    scaffoldBackgroundColor: dark ? GarajColors.bgDark : GarajColors.bg,
    fontFamily: GarajFonts.sans,

    // 15 px es el mínimo de la guía en móvil: el técnico lee esto con las manos sucias y el
    // teléfono a medio metro, no a treinta centímetros como en un escritorio.
    textTheme: const TextTheme(
      headlineLarge: TextStyle(
        fontFamily: GarajFonts.display,
        fontWeight: FontWeight.w700,
        letterSpacing: -0.8,
      ),
      titleLarge: TextStyle(
        fontFamily: GarajFonts.display,
        fontWeight: FontWeight.w700,
        letterSpacing: -0.4,
      ),
      bodyLarge: TextStyle(fontSize: 16, height: 1.45),
      bodyMedium: TextStyle(fontSize: 15, height: 1.45),
      labelLarge: TextStyle(fontWeight: FontWeight.w600),
    ),

    appBarTheme: AppBarTheme(
      backgroundColor: dark ? GarajColors.surfaceDark : GarajColors.surface,
      foregroundColor: dark ? Colors.white : GarajColors.ink,
      elevation: 0,
      scrolledUnderElevation: 0,
      shape: Border(bottom: BorderSide(color: border)),
      titleTextStyle: TextStyle(
        fontFamily: GarajFonts.display,
        fontWeight: FontWeight.w700,
        fontSize: 20,
        letterSpacing: -0.4,
        color: dark ? Colors.white : GarajColors.ink,
      ),
    ),

    // Tarjetas con borde y sin sombra: en una lista larga de órdenes, doce sombras apiladas
    // ensucian la pantalla y no separan mejor que una línea de un píxel.
    cardTheme: CardThemeData(
      color: dark ? GarajColors.surfaceDark : GarajColors.surface,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(
        side: BorderSide(color: border),
        borderRadius: const BorderRadius.all(Radius.circular(10)),
      ),
    ),

    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: dark ? GarajColors.surfaceDark : GarajColors.surface,
      border: OutlineInputBorder(
        borderSide: BorderSide(color: border),
        borderRadius: const BorderRadius.all(Radius.circular(6)),
      ),
      enabledBorder: OutlineInputBorder(
        borderSide: BorderSide(color: border),
        borderRadius: const BorderRadius.all(Radius.circular(6)),
      ),
    ),

    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(48),
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.all(Radius.circular(6)),
        ),
        textStyle: const TextStyle(fontWeight: FontWeight.w600, fontSize: 16),
      ),
    ),

    dividerTheme: DividerThemeData(color: border, space: 1, thickness: 1),
  );
}

final garajTheme = _theme(Brightness.light);
final garajDarkTheme = _theme(Brightness.dark);
