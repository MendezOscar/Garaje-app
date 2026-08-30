// GarajApp — colores y tipografía de marca (Flutter)
// mobile/lib/core/theme/garaj_brand.dart
import 'package:flutter/material.dart';

class GarajColors {
  static const brand = Color(0xFF1F6FEB);
  static const brandDeep = Color(0xFF124293);
  static const ink = Color(0xFF14171C);

  static const bg = Color(0xFFF7F8FA);
  static const surface = Color(0xFFFFFFFF);
  static const surfaceAlt = Color(0xFFEEF0F4);
  static const border = Color(0xFFD9DDE4);
  // 4,88:1 sobre el fondo claro: el #6B7480 anterior se quedaba en 4,46:1, bajo la AA.
  static const textMuted = Color(0xFF646E7A);

  static const warning = Color(0xFFF2A31A); // WaitingApproval / WaitingParts
  static const success = Color(0xFF1FA971); // Ready / Delivered
  static const danger = Color(0xFFC0392B);  // Cancelled / sin stock
}

class GarajFonts {
  static const display = 'SpaceGrotesk';
  static const sans = 'IBMPlexSans';
  static const mono = 'IBMPlexMono'; // folios, montos, cantidades
}

final garajTheme = ThemeData(
  useMaterial3: true,
  scaffoldBackgroundColor: GarajColors.bg,
  fontFamily: GarajFonts.sans,
  colorScheme: ColorScheme.fromSeed(
    seedColor: GarajColors.brand,
    primary: GarajColors.brand,
    error: GarajColors.danger,
    surface: GarajColors.surface,
  ),
  textTheme: const TextTheme(
    headlineLarge: TextStyle(fontFamily: GarajFonts.display, fontWeight: FontWeight.w700, letterSpacing: -0.8),
    titleLarge: TextStyle(fontFamily: GarajFonts.display, fontWeight: FontWeight.w700, letterSpacing: -0.4),
    bodyLarge: TextStyle(fontSize: 16, height: 1.45),
    labelLarge: TextStyle(fontWeight: FontWeight.w600),
  ),
  cardTheme: const CardThemeData(
    color: GarajColors.surface,
    elevation: 0,
    shape: RoundedRectangleBorder(
      side: BorderSide(color: GarajColors.border),
      borderRadius: BorderRadius.all(Radius.circular(10)),
    ),
  ),
);
