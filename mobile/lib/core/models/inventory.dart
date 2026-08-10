// Espejo de los DTOs de la Fase 3.

class Part {
  const Part({
    required this.id,
    required this.sku,
    required this.name,
    required this.unit,
    required this.costPrice,
    required this.salePrice,
    required this.totalQuantity,
    required this.isActive,
    this.brand,
    this.category,
  });

  factory Part.fromJson(Map<String, dynamic> json) => Part(
        id: json['id'] as String,
        sku: json['sku'] as String,
        name: json['name'] as String,
        brand: json['brand'] as String?,
        category: json['category'] as String?,
        unit: json['unit'] as String,
        costPrice: (json['costPrice'] as num?)?.toDouble() ?? 0,
        salePrice: (json['salePrice'] as num).toDouble(),
        totalQuantity: (json['totalQuantity'] as num).toDouble(),
        isActive: json['isActive'] as bool? ?? true,
      );

  final String id;
  final String sku;
  final String name;
  final String? brand;
  final String? category;
  final String unit;

  /// Costo de referencia: lo que se pagó en la última entrada. El costo con el que se
  /// calcula el margen de una orden es el que quedó congelado en ella.
  final double costPrice;
  final double salePrice;

  /// Existencia sumada de las sucursales que el usuario ve. Para el técnico, la suya.
  final double totalQuantity;
  final bool isActive;

  bool get isOutOfStock => totalQuantity <= 0;
}

/// La existencia de un repuesto en una sucursal. El saldo no se edita: se mueve.
class StockItem {
  const StockItem({
    required this.partId,
    required this.sku,
    required this.partName,
    required this.unit,
    required this.branchId,
    required this.branchName,
    required this.quantity,
    required this.minQuantity,
    required this.costPrice,
    required this.salePrice,
    required this.isBelowMinimum,
    this.brand,
    this.category,
    this.location,
  });

  factory StockItem.fromJson(Map<String, dynamic> json) => StockItem(
        partId: json['partId'] as String,
        sku: json['sku'] as String,
        partName: json['partName'] as String,
        brand: json['brand'] as String?,
        category: json['category'] as String?,
        unit: json['unit'] as String,
        branchId: json['branchId'] as String,
        branchName: json['branchName'] as String,
        quantity: (json['quantity'] as num).toDouble(),
        minQuantity: (json['minQuantity'] as num).toDouble(),
        location: json['location'] as String?,
        costPrice: (json['costPrice'] as num?)?.toDouble() ?? 0,
        salePrice: (json['salePrice'] as num?)?.toDouble() ?? 0,
        isBelowMinimum: json['isBelowMinimum'] as bool? ?? false,
      );

  final String partId;
  final String sku;
  final String partName;
  final String? brand;
  final String? category;
  final String unit;
  final String branchId;
  final String branchName;
  final double quantity;
  final double minQuantity;
  final String? location;
  final double costPrice;
  final double salePrice;
  final bool isBelowMinimum;
}

/// Tipos de movimiento. Mismos valores que `Garaj.Domain.Enums.StockMovementType`.
enum StockMovementType {
  entry(1, 'Entrada'),
  exit(2, 'Salida'),
  adjustment(3, 'Ajuste'),
  transferIn(4, 'Traslado recibido'),
  transferOut(5, 'Traslado enviado');

  const StockMovementType(this.value, this.label);

  final int value;
  final String label;

  static StockMovementType fromValue(int value) =>
      StockMovementType.values.firstWhere((t) => t.value == value, orElse: () => entry);
}

/// Una línea del kardex: qué pasó, cuánto sumó o restó y con qué saldo quedó.
class StockMovement {
  const StockMovement({
    required this.id,
    required this.type,
    required this.branchName,
    required this.signedQuantity,
    required this.resultingQuantity,
    required this.movedAt,
    required this.movedByName,
    this.reference,
    this.notes,
    this.workOrderNumber,
    this.counterpartBranchName,
  });

  factory StockMovement.fromJson(Map<String, dynamic> json) => StockMovement(
        id: json['id'] as String,
        type: StockMovementType.fromValue(json['type'] as int),
        branchName: json['branchName'] as String,
        signedQuantity: (json['signedQuantity'] as num).toDouble(),
        resultingQuantity: (json['resultingQuantity'] as num).toDouble(),
        movedAt: DateTime.parse(json['movedAt'] as String),
        movedByName: json['movedByName'] as String? ?? '',
        reference: json['reference'] as String?,
        notes: json['notes'] as String?,
        workOrderNumber: json['workOrderNumber'] as String?,
        counterpartBranchName: json['counterpartBranchName'] as String?,
      );

  final String id;
  final StockMovementType type;
  final String branchName;
  final double signedQuantity;
  final double resultingQuantity;
  final DateTime movedAt;
  final String movedByName;
  final String? reference;
  final String? notes;
  final String? workOrderNumber;
  final String? counterpartBranchName;
}

class WorkOrderPart {
  const WorkOrderPart({
    required this.id,
    required this.partId,
    required this.sku,
    required this.partName,
    required this.unit,
    required this.quantity,
    required this.unitPrice,
    required this.total,
    this.taskTitle,
  });

  factory WorkOrderPart.fromJson(Map<String, dynamic> json) => WorkOrderPart(
        id: json['id'] as String,
        partId: json['partId'] as String,
        sku: json['sku'] as String,
        partName: json['partName'] as String,
        unit: json['unit'] as String,
        quantity: (json['quantity'] as num).toDouble(),
        unitPrice: (json['unitPrice'] as num).toDouble(),
        total: (json['total'] as num).toDouble(),
        taskTitle: json['taskTitle'] as String?,
      );

  final String id;
  final String partId;
  final String sku;
  final String partName;
  final String unit;
  final double quantity;
  final double unitPrice;
  final double total;
  final String? taskTitle;
}
