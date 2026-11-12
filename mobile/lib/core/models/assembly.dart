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
        technicianPay: (json['technicianPay'] as num?)?.toDouble(),
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

  /// Lo que se le paga al técnico por esta moto. **Null para el propio técnico**: el
  /// servidor no se lo manda, porque el trato de cada uno se liquida en su corte.
  final double? technicianPay;
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
        pay: (json['pay'] as num?)?.toDouble(),
        items: (json['items'] as List<dynamic>)
            .map((e) => MotorcycleAssembly.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final DateTime from;
  final DateTime to;
  final int count;

  /// Lo que se le debe por esas motos. Es su número, no el del taller.
  /// Lo que lleva ganado. Null cuando lo pregunta el propio técnico.
  final double? pay;
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

// ---------------------------------------------------------------------------
// Lo del dueño: la lista del taller, los cortes y el pago.
//
// Lo que el técnico ve es lo que armó él. De aquí para abajo es lo otro: las motos de
// todos con sus dos precios, la factura al contratista y lo que se le paga a cada quien.
// El servidor ya decide quién lo recibe; estas clases solo saben leerlo.
// ---------------------------------------------------------------------------

/// Una moto en la lista del taller, con su estado de corte.
class AssemblyRow {
  const AssemblyRow({
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
    required this.charge,
    required this.technicianPay,
    required this.expense,
    required this.billed,
    required this.paid,
    this.notes,
  });

  factory AssemblyRow.fromJson(Map<String, dynamic> json) => AssemblyRow(
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
        charge: (json['charge'] as num).toDouble(),
        technicianPay: (json['technicianPay'] as num).toDouble(),
        expense: (json['expense'] as num?)?.toDouble() ?? 0,
        billed: json['billed'] as bool? ?? false,
        paid: json['paid'] as bool? ?? false,
        notes: json['notes'] as String?,
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

  /// Lo que se le cobra al contratista por armarla.
  final double charge;

  /// Lo que se le paga al técnico que la armó.
  final double technicianPay;

  /// Lo que le costó al taller aparte del pago. Nunca viaja al contratista.
  final double expense;

  /// Si ya entró en un corte de facturación, y si ya entró en uno de pago. Una moto
  /// facturada no se puede corregir ni borrar: el corte la congeló.
  final bool billed;
  final bool paid;
  final String? notes;

  double get margin => charge - technicianPay - expense;
}

/// Una página de la lista, con lo que suma el filtro completo y no solo lo que se ve.
class AssemblyPage {
  const AssemblyPage({
    required this.items,
    required this.total,
    required this.page,
    required this.pageSize,
    required this.totalCharge,
    required this.totalPay,
    required this.totalExpense,
  });

  factory AssemblyPage.fromJson(Map<String, dynamic> json) => AssemblyPage(
        items: (json['items'] as List<dynamic>)
            .map((e) => AssemblyRow.fromJson(e as Map<String, dynamic>))
            .toList(),
        total: json['total'] as int,
        page: json['page'] as int,
        pageSize: json['pageSize'] as int,
        totalCharge: (json['totalCharge'] as num?)?.toDouble() ?? 0,
        totalPay: (json['totalPay'] as num?)?.toDouble() ?? 0,
        totalExpense: (json['totalExpense'] as num?)?.toDouble() ?? 0,
      );

  final List<AssemblyRow> items;
  final int total;
  final int page;
  final int pageSize;
  final double totalCharge;
  final double totalPay;
  final double totalExpense;

  /// Lo que de verdad le queda al taller con este filtro.
  double get margin => totalCharge - totalPay - totalExpense;

  bool get hayMas => page * pageSize < total;
}

/// Qué clase de corte: la factura al contratista o el pago a un técnico.
enum AssemblySettlementKind {
  billing(1),
  technicianPay(2);

  const AssemblySettlementKind(this.value);

  final int value;

  static AssemblySettlementKind fromValue(int value) =>
      value == 2 ? AssemblySettlementKind.technicianPay : AssemblySettlementKind.billing;

  bool get esFactura => this == AssemblySettlementKind.billing;
}

/// Un corte ya cerrado: una factura al contratista, o el pago de un técnico.
class AssemblySettlement {
  const AssemblySettlement({
    required this.id,
    required this.kind,
    required this.periodFrom,
    required this.periodTo,
    required this.count,
    required this.total,
    required this.closedAt,
    this.contractorId,
    this.contractorName,
    this.technicianUserId,
    this.technicianName,
    this.reference,
    this.closedByName,
    this.collectedOn,
    this.collectedReference,
  });

  factory AssemblySettlement.fromJson(Map<String, dynamic> json) => AssemblySettlement(
        id: json['id'] as String,
        kind: AssemblySettlementKind.fromValue(json['kind'] as int),
        periodFrom: DateTime.parse(json['periodFrom'] as String),
        periodTo: DateTime.parse(json['periodTo'] as String),
        count: json['count'] as int,
        total: (json['total'] as num).toDouble(),
        closedAt: DateTime.parse(json['closedAt'] as String),
        contractorId: json['contractorId'] as String?,
        contractorName: json['contractorName'] as String?,
        technicianUserId: json['technicianUserId'] as String?,
        technicianName: json['technicianName'] as String?,
        reference: json['reference'] as String?,
        closedByName: json['closedByName'] as String?,
        collectedOn: json['collectedOn'] == null
            ? null
            : DateTime.parse(json['collectedOn'] as String),
        collectedReference: json['collectedReference'] as String?,
      );

  final String id;
  final AssemblySettlementKind kind;
  final DateTime periodFrom;
  final DateTime periodTo;
  final int count;
  final double total;
  final DateTime closedAt;
  final String? contractorId;
  final String? contractorName;
  final String? technicianUserId;
  final String? technicianName;
  final String? reference;
  final String? closedByName;

  /// El día en que el contratista pagó la factura, o null mientras siga por cobrar. Siempre
  /// null en un corte de pago: ahí cerrar ya es entregar la plata.
  final DateTime? collectedOn;
  final String? collectedReference;

  /// De quién es el corte: el contratista al que se le factura o el técnico al que se le paga.
  String get quien => contractorName ?? technicianName ?? 'Sin nombre';

  bool get porCobrar => kind.esFactura && collectedOn == null;
}

/// Lo que se le debe a cada técnico por un periodo.
class AssemblyPayReport {
  const AssemblyPayReport({
    required this.from,
    required this.to,
    required this.count,
    required this.total,
    required this.lines,
  });

  factory AssemblyPayReport.fromJson(Map<String, dynamic> json) => AssemblyPayReport(
        from: DateTime.parse(json['from'] as String),
        to: DateTime.parse(json['to'] as String),
        count: json['count'] as int,
        total: (json['total'] as num).toDouble(),
        lines: (json['lines'] as List<dynamic>)
            .map((e) => AssemblyPayLine.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final DateTime from;
  final DateTime to;
  final int count;
  final double total;
  final List<AssemblyPayLine> lines;
}

class AssemblyPayLine {
  const AssemblyPayLine({
    required this.technicianUserId,
    required this.technicianName,
    required this.count,
    required this.pay,
  });

  factory AssemblyPayLine.fromJson(Map<String, dynamic> json) => AssemblyPayLine(
        technicianUserId: json['technicianUserId'] as String,
        technicianName: json['technicianName'] as String,
        count: json['count'] as int,
        pay: (json['pay'] as num).toDouble(),
      );

  final String technicianUserId;
  final String technicianName;
  final int count;
  final double pay;
}

/// El filtro del dueño: el periodo y por quién se pregunta.
class AssemblyFilter {
  const AssemblyFilter({
    this.from,
    this.to,
    this.contractorId,
    this.technicianUserId,
    this.warehouseId,
    this.modelId,
    this.serial,
    this.onlyUnbilled = false,
    this.onlyUnpaid = false,
    this.page = 1,
    this.pageSize = 50,
  });

  final DateTime? from;
  final DateTime? to;
  final String? contractorId;
  final String? technicianUserId;
  final String? warehouseId;
  final String? modelId;
  final String? serial;

  /// Solo lo que todavía no entra en un corte: lo que falta por facturar, o por pagar.
  final bool onlyUnbilled;
  final bool onlyUnpaid;
  final int page;
  final int pageSize;

  AssemblyFilter copyWith({
    DateTime? from,
    DateTime? to,
    String? contractorId,
    String? technicianUserId,
    String? warehouseId,
    String? modelId,
    String? serial,
    bool? onlyUnbilled,
    bool? onlyUnpaid,
    int? page,
    bool limpiarContratista = false,
    bool limpiarTecnico = false,
    bool limpiarBodega = false,
    bool limpiarModelo = false,
  }) =>
      AssemblyFilter(
        from: from ?? this.from,
        to: to ?? this.to,
        contractorId: limpiarContratista ? null : contractorId ?? this.contractorId,
        technicianUserId: limpiarTecnico ? null : technicianUserId ?? this.technicianUserId,
        warehouseId: limpiarBodega ? null : warehouseId ?? this.warehouseId,
        modelId: limpiarModelo ? null : modelId ?? this.modelId,
        serial: serial ?? this.serial,
        onlyUnbilled: onlyUnbilled ?? this.onlyUnbilled,
        onlyUnpaid: onlyUnpaid ?? this.onlyUnpaid,
        page: page ?? this.page,
        pageSize: pageSize,
      );

  Map<String, dynamic> toQuery() => {
        if (from != null) 'from': diaApi(from!),
        if (to != null) 'to': diaApi(to!),
        if (contractorId != null) 'contractorId': contractorId,
        if (technicianUserId != null) 'technicianUserId': technicianUserId,
        if (warehouseId != null) 'warehouseId': warehouseId,
        if (modelId != null) 'modelId': modelId,
        if (serial != null && serial!.trim().isNotEmpty) 'serial': serial!.trim(),
        if (onlyUnbilled) 'onlyUnbilled': true,
        if (onlyUnpaid) 'onlyUnpaid': true,
        'page': page,
        'pageSize': pageSize,
      };

  /// Riverpod guarda los proveedores por valor del argumento: sin esto cada `setState`
  /// pediría la lista otra vez aunque el filtro sea el mismo.
  @override
  bool operator ==(Object other) =>
      other is AssemblyFilter &&
      other.from == from &&
      other.to == to &&
      other.contractorId == contractorId &&
      other.technicianUserId == technicianUserId &&
      other.warehouseId == warehouseId &&
      other.modelId == modelId &&
      other.serial == serial &&
      other.onlyUnbilled == onlyUnbilled &&
      other.onlyUnpaid == onlyUnpaid &&
      other.page == page &&
      other.pageSize == pageSize;

  @override
  int get hashCode => Object.hash(
        from,
        to,
        contractorId,
        technicianUserId,
        warehouseId,
        modelId,
        serial,
        onlyUnbilled,
        onlyUnpaid,
        page,
        pageSize,
      );
}

/// Un día como lo espera la API: `2026-11-11`, sin hora y sin desplazamiento.
String diaApi(DateTime value) =>
    '${value.year}-${value.month.toString().padLeft(2, '0')}-'
    '${value.day.toString().padLeft(2, '0')}';

// ---------------------------------------------------------------------------
// El catálogo del contrato: con quién se trabaja, qué modelos se arman, en qué bodegas, y
// la tabla de precios. Es del Dueño entero: de aquí salen los dos números de cada moto.
// ---------------------------------------------------------------------------

/// Una fila de la tabla de un contratista: lo que paga y lo que se le paga al técnico.
class AssemblyRate {
  const AssemblyRate({
    required this.modelId,
    required this.modelName,
    required this.charge,
    required this.technicianPay,
  });

  factory AssemblyRate.fromJson(Map<String, dynamic> json) => AssemblyRate(
        modelId: json['modelId'] as String,
        modelName: json['modelName'] as String,
        charge: (json['charge'] as num).toDouble(),
        technicianPay: (json['technicianPay'] as num).toDouble(),
      );

  final String modelId;
  final String modelName;
  final double charge;
  final double technicianPay;
}

class AssemblyContractor {
  const AssemblyContractor({
    required this.id,
    required this.name,
    required this.isActive,
    required this.rates,
    this.notes,
    this.branchId,
  });

  factory AssemblyContractor.fromJson(Map<String, dynamic> json) => AssemblyContractor(
        id: json['id'] as String,
        name: json['name'] as String,
        isActive: json['isActive'] as bool? ?? true,
        rates: (json['rates'] as List<dynamic>? ?? const [])
            .map((e) => AssemblyRate.fromJson(e as Map<String, dynamic>))
            .toList(),
        notes: json['notes'] as String?,
        branchId: json['branchId'] as String?,
      );

  final String id;
  final String name;
  final bool isActive;
  final List<AssemblyRate> rates;
  final String? notes;

  /// La sucursal que lleva el contrato. De ahí sale la de cada moto, para no preguntársela
  /// al técnico, que está en la bodega del contratista y no la sabe.
  final String? branchId;
}

class AssemblyModel {
  const AssemblyModel({
    required this.id,
    required this.name,
    required this.isActive,
    this.vinPrefix,
  });

  factory AssemblyModel.fromJson(Map<String, dynamic> json) => AssemblyModel(
        id: json['id'] as String,
        name: json['name'] as String,
        isActive: json['isActive'] as bool? ?? true,
        vinPrefix: json['vinPrefix'] as String?,
      );

  final String id;
  final String name;
  final bool isActive;

  /// Los primeros caracteres del VIN de este modelo. Vacío se admite: entonces el modelo se
  /// propone solo a partir de la segunda moto de cada lote.
  final String? vinPrefix;
}

class AssemblyWarehouse {
  const AssemblyWarehouse({required this.id, required this.name, required this.isActive});

  factory AssemblyWarehouse.fromJson(Map<String, dynamic> json) => AssemblyWarehouse(
        id: json['id'] as String,
        name: json['name'] as String,
        isActive: json['isActive'] as bool? ?? true,
      );

  final String id;
  final String name;
  final bool isActive;
}
