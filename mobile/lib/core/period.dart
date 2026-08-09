/// Filtro de fechas de las listas: hoy, esta semana, este mes o todo.
///
/// Corta igual que los reportes —la semana arranca el domingo y el mes el día 1— para que
/// "esta semana" signifique lo mismo en el teléfono que en el web.
enum Period {
  all('Todo'),
  today('Hoy'),
  week('Semana'),
  month('Mes');

  const Period(this.label);

  final String label;

  /// Desde cuándo listar. `null` en «Todo»: sin filtro.
  DateTime? get from {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);

    return switch (this) {
      Period.all => null,
      Period.today => today,
      // weekday va de 1 (lunes) a 7 (domingo): el domingo no se resta nada.
      Period.week => today.subtract(Duration(days: today.weekday % 7)),
      Period.month => DateTime(now.year, now.month),
    };
  }
}
