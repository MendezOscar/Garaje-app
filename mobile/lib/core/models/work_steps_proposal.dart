/// Lo que el asistente propone a partir del diagnóstico.
///
/// Es un borrador: nada de esto está en la orden hasta que alguien lo acepta. Por eso el botón
/// de la pantalla dice «Aceptar» y no «Guardar».
class WorkStepsProposal {
  const WorkStepsProposal({
    required this.id,
    required this.available,
    required this.message,
    required this.tasks,
    required this.parts,
    required this.note,
    required this.creditsLeftToday,
    this.missing = const [],
  });

  factory WorkStepsProposal.fromJson(Map<String, dynamic> json) => WorkStepsProposal(
        id: json['id'] as String?,
        available: json['available'] as bool? ?? false,
        message: json['message'] as String?,
        tasks: ((json['tasks'] as List<dynamic>?) ?? [])
            .map((e) => ProposedTask.fromJson(e as Map<String, dynamic>))
            .toList(),
        parts: ((json['parts'] as List<dynamic>?) ?? [])
            .map((e) => ProposedPart.fromJson(e as Map<String, dynamic>))
            .toList(),
        note: json['note'] as String?,
        creditsLeftToday: (json['creditsLeftToday'] as num?)?.toInt() ?? 0,
        missing: ((json['missing'] as List<dynamic>?) ?? [])
            .map((e) => MissingPart.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  /// Null cuando no hubo propuesta: entonces manda [message].
  final String? id;

  /// En false no hay error que enseñar: es que se acabaron las consultas del día o que falta
  /// el diagnóstico. El porqué viene en [message], escrito para el taller.
  final bool available;
  final String? message;

  final List<ProposedTask> tasks;
  final List<ProposedPart> parts;

  /// Lo que el asistente quiso aclarar: qué falta averiguar, qué repuesto no estaba.
  final String? note;

  final int creditsLeftToday;

  /// Lo que el trabajo lleva y el taller no tiene en catálogo. Va aparte de [parts] para que
  /// nadie los confunda con existencia: estos hay que comprarlos o darlos de alta.
  final List<MissingPart> missing;
}

class MissingPart {
  const MissingPart({required this.name, required this.quantity, required this.forTask});

  factory MissingPart.fromJson(Map<String, dynamic> json) => MissingPart(
        name: json['name'] as String,
        quantity: (json['quantity'] as num?)?.toDouble() ?? 0,
        forTask: json['forTask'] as String? ?? '',
      );

  /// Con el nombre con el que se pide en una casa de repuestos.
  final String name;
  final double quantity;
  final String forTask;
}

/// Lo que el taller ha cobrado por este mismo trabajo, de sus propias órdenes entregadas.
/// Lo calcula el servidor: no es un precio que la IA se saque de la manga.
class TaskHistory {
  const TaskHistory({
    required this.times,
    this.priceFrom,
    this.priceTo,
    this.typicalHours,
  });

  factory TaskHistory.fromJson(Map<String, dynamic> json) => TaskHistory(
        times: (json['times'] as num?)?.toInt() ?? 0,
        priceFrom: (json['priceFrom'] as num?)?.toDouble(),
        priceTo: (json['priceTo'] as num?)?.toDouble(),
        typicalHours: (json['typicalHours'] as num?)?.toDouble(),
      );

  final int times;
  final double? priceFrom;
  final double? priceTo;
  final double? typicalHours;

  /// Como se lee en una línea: «3 veces antes · cobrado L 525.00 a L 800.00».
  String get resumen {
    final veces = '$times ${times == 1 ? 'vez' : 'veces'} antes';

    if (priceFrom == null || priceTo == null) {
      return typicalHours == null ? 'Hecho $veces' : 'Hecho $veces · $typicalHours h';
    }

    final precio = priceFrom == priceTo
        ? 'L ${priceFrom!.toStringAsFixed(2)}'
        : 'L ${priceFrom!.toStringAsFixed(2)} a L ${priceTo!.toStringAsFixed(2)}';

    return '$veces · cobrado $precio';
  }
}

class ProposedTask {
  ProposedTask({
    required this.title,
    required this.description,
    required this.laborServiceId,
    required this.laborServiceName,
    required this.hours,
    this.history,
    this.incluido = true,
  });

  factory ProposedTask.fromJson(Map<String, dynamic> json) => ProposedTask(
        title: json['title'] as String,
        description: json['description'] as String?,
        laborServiceId: json['laborServiceId'] as String?,
        laborServiceName: json['laborServiceName'] as String?,
        hours: (json['hours'] as num?)?.toDouble() ?? 0,
        history: json['history'] == null
            ? null
            : TaskHistory.fromJson(json['history'] as Map<String, dynamic>),
      );

  final String title;
  final String? description;

  /// El servicio del catálogo con el que se cobra. Null es correcto y pasa seguido: vale más
  /// un paso sin servicio que uno colgado del equivocado.
  final String? laborServiceId;
  final String? laborServiceName;

  double hours;

  /// Null cuando el taller nunca lo ha hecho, o cuando el paso no salió del catálogo. Sin
  /// historia no se enseña ningún número: la diferencia entre callar y adivinar.
  final TaskHistory? history;

  /// Si se queda. Se edita en la pantalla: tachar un paso es lo primero que hace quien revisa.
  bool incluido;

  Map<String, dynamic> toJson() => {
        'title': title,
        'description': description,
        'laborServiceId': laborServiceId,
        'estimatedHours': hours == 0 ? null : hours,
      };
}

class ProposedPart {
  const ProposedPart({
    required this.partId,
    required this.sku,
    required this.name,
    required this.unit,
    required this.quantity,
    required this.available,
    required this.forTask,
  });

  factory ProposedPart.fromJson(Map<String, dynamic> json) => ProposedPart(
        partId: json['partId'] as String,
        sku: json['sku'] as String,
        name: json['name'] as String,
        unit: json['unit'] as String,
        quantity: (json['quantity'] as num?)?.toDouble() ?? 0,
        available: (json['available'] as num?)?.toDouble() ?? 0,
        forTask: json['forTask'] as String? ?? '',
      );

  final String partId;
  final String sku;
  final String name;
  final String unit;
  final double quantity;

  /// Existencia en la bodega de la sucursal de la orden.
  final double available;

  /// Para qué paso es, con las palabras del asistente.
  final String forTask;
}
