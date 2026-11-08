/// El armado de motos: lo que el técnico registra parado en la bodega.
///
/// Lo que reemplaza la foto al grupo de WhatsApp y el Excel de la noche. El dato que el
/// técnico ya tiene en la mano se guarda donde va, y el reporte del mes sale solo.
library;

/// Una moto armada.
class MotorcycleAssembly {
  const MotorcycleAssembly({
    required this.id,
    required this.assembledOn,
    required this.serial,
    required this.modelId,
    required this.modelName,
    required this.contractorId,
    required this.contractorName,
    required this.warehouseId,
    required this.warehouseName,
    required this.technicianUserId,
    required this.technicianName,
    required this.technicianPay,
    this.notes,
    this.charge,
  });

  factory MotorcycleAssembly.fromJson(Map<String, dynamic> json) => MotorcycleAssembly(
        id: json['id'] as String,
        assembledOn: DateTime.parse(json['assembledOn'] as String),
        serial: json['serial'] as String,
        modelId: json['modelId'] as String,
        modelName: json['modelName'] as String,
        contractorId: json['contractorId'] as String,
        contractorName: json['contractorName'] as String,
        warehouseId: json['warehouseId'] as String,
        warehouseName: json['warehouseName'] as String,
        technicianUserId: json['technicianUserId'] as String,
        technicianName: json['technicianName'] as String,
        technicianPay: (json['technicianPay'] as num).toDouble(),
        notes: json['notes'] as String?,
        charge: (json['charge'] as num?)?.toDouble(),
      );

  final String id;
  final DateTime assembledOn;
  final String serial;
  final String modelId;
  final String modelName;
  final String contractorId;
  final String contractorName;
  final String warehouseId;
  final String warehouseName;
  final String technicianUserId;
  final String technicianName;

  /// Lo que se le paga al técnico por esta moto.
  final double technicianPay;
  final String? notes;

  /// Lo que se le cobra al contratista. **Null para el técnico**: el servidor no se lo manda.
  final double? charge;
}

/// Una fila de una lista para elegir: contratista, modelo o bodega. Sin precios.
class AssemblyName {
  const AssemblyName({required this.id, required this.name});

  factory AssemblyName.fromJson(Map<String, dynamic> json) =>
      AssemblyName(id: json['id'] as String, name: json['name'] as String);

  final String id;
  final String name;
}

/// Las listas con que se llena el registro sin teclear.
class AssemblyPickLists {
  const AssemblyPickLists({
    required this.contractors,
    required this.models,
    required this.warehouses,
    this.technicians = const [],
  });

  factory AssemblyPickLists.fromJson(Map<String, dynamic> json) => AssemblyPickLists(
        contractors: _lista(json['contractors']),
        models: _lista(json['models']),
        warehouses: _lista(json['warehouses']),
        technicians: _lista(json['technicians'] ?? const []),
      );

  static List<AssemblyName> _lista(dynamic v) => (v as List<dynamic>)
      .map((e) => AssemblyName.fromJson(e as Map<String, dynamic>))
      .toList();

  final List<AssemblyName> contractors;
  final List<AssemblyName> models;
  final List<AssemblyName> warehouses;

  /// A quién se le puede apuntar la moto. **Vacía para el técnico**: él solo registra lo
  /// suyo, y el servidor ignora lo que venga. Llena para quien manda, que sigue recibiendo
  /// fotos por WhatsApp y registra él lo que armó otro.
  final List<AssemblyName> technicians;

  bool get listo => models.isNotEmpty && warehouses.isNotEmpty && contractors.isNotEmpty;
}

/// Lo que lleva armado quien pregunta, y lo que suma para él.
class MyAssemblies {
  const MyAssemblies({
    required this.from,
    required this.to,
    required this.count,
    required this.pay,
    required this.items,
  });

  factory MyAssemblies.fromJson(Map<String, dynamic> json) => MyAssemblies(
        from: DateTime.parse(json['from'] as String),
        to: DateTime.parse(json['to'] as String),
        count: json['count'] as int,
        pay: (json['pay'] as num).toDouble(),
        items: (json['items'] as List<dynamic>)
            .map((e) => MotorcycleAssembly.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final DateTime from;
  final DateTime to;
  final int count;

  /// Lo que se le debe por esas motos. Es su número, no el del taller.
  final double pay;
  final List<MotorcycleAssembly> items;
}

/// El modelo que probablemente lleva una serie, aprendido de lo ya registrado en el taller.
///
/// Siempre una propuesta: la confirma quien tiene la moto delante. Una serie mal leída es
/// peor que una tecleada.
class ModelGuess {
  const ModelGuess({this.modelId, this.modelName, this.seenBefore = 0});

  factory ModelGuess.fromJson(Map<String, dynamic> json) => ModelGuess(
        modelId: json['modelId'] as String?,
        modelName: json['modelName'] as String?,
        seenBefore: json['seenBefore'] as int? ?? 0,
      );

  final String? modelId;
  final String? modelName;
  final int seenBefore;
}
