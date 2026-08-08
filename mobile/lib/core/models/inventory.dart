// Espejo de los DTOs de la Fase 3.

class Part {
  const Part({
    required this.id,
    required this.sku,
    required this.name,
    required this.unit,
    required this.salePrice,
    required this.totalQuantity,
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
        salePrice: (json['salePrice'] as num).toDouble(),
        totalQuantity: (json['totalQuantity'] as num).toDouble(),
      );

  final String id;
  final String sku;
  final String name;
  final String? brand;
  final String? category;
  final String unit;
  final double salePrice;

  /// Existencia sumada de las sucursales que el usuario ve. Para el técnico, la suya.
  final double totalQuantity;

  bool get isOutOfStock => totalQuantity <= 0;
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
