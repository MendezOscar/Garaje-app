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
}

class ProposedTask {
  ProposedTask({
    required this.title,
    required this.description,
    required this.laborServiceId,
    required this.laborServiceName,
    required this.hours,
    this.incluido = true,
  });

  factory ProposedTask.fromJson(Map<String, dynamic> json) => ProposedTask(
        title: json['title'] as String,
        description: json['description'] as String?,
        laborServiceId: json['laborServiceId'] as String?,
        laborServiceName: json['laborServiceName'] as String?,
        hours: (json['hours'] as num?)?.toDouble() ?? 0,
      );

  final String title;
  final String? description;

  /// El servicio del catálogo con el que se cobra. Null es correcto y pasa seguido: vale más
  /// un paso sin servicio que uno colgado del equivocado.
  final String? laborServiceId;
  final String? laborServiceName;

  double hours;

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
