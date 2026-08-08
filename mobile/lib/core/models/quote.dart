// Espejo de los DTOs de la Fase 4.

enum QuoteStatus {
  draft(1, 'Borrador'),
  sent(2, 'Enviada'),
  approved(3, 'Aprobada'),
  rejected(4, 'Rechazada'),
  expired(5, 'Vencida');

  const QuoteStatus(this.value, this.label);

  final int value;
  final String label;

  static QuoteStatus fromValue(int value) =>
      QuoteStatus.values.firstWhere((s) => s.value == value, orElse: () => QuoteStatus.draft);
}

enum LineType {
  part(1, 'Repuesto'),
  labor(2, 'Mano de obra');

  const LineType(this.value, this.label);

  final int value;
  final String label;

  static LineType fromValue(int value) =>
      LineType.values.firstWhere((t) => t.value == value, orElse: () => LineType.part);
}

class QuoteLine {
  const QuoteLine({
    required this.lineType,
    required this.description,
    required this.quantity,
    required this.unitPrice,
    required this.total,
  });

  factory QuoteLine.fromJson(Map<String, dynamic> json) => QuoteLine(
        lineType: LineType.fromValue(json['lineType'] as int),
        description: json['description'] as String,
        quantity: (json['quantity'] as num).toDouble(),
        unitPrice: (json['unitPrice'] as num).toDouble(),
        total: (json['total'] as num).toDouble(),
      );

  final LineType lineType;
  final String description;
  final double quantity;
  final double unitPrice;
  final double total;
}

class Quote {
  const Quote({
    required this.id,
    required this.number,
    required this.status,
    required this.customerName,
    required this.customerPhone,
    required this.total,
    required this.subtotal,
    required this.taxRate,
    required this.taxTotal,
    required this.currency,
    required this.lines,
    required this.isExpired,
    this.vehicleLabel,
    this.workOrderNumber,
    this.notes,
    this.validUntil,
    this.respondedAt,
    this.customerResponseNote,
    this.publicUrl,
  });

  factory Quote.fromJson(Map<String, dynamic> json) => Quote(
        id: json['id'] as String,
        number: json['number'] as String,
        status: QuoteStatus.fromValue(json['status'] as int),
        customerName: json['customerName'] as String,
        customerPhone: json['customerPhone'] as String,
        vehicleLabel: json['vehicleLabel'] as String?,
        workOrderNumber: json['workOrderNumber'] as String?,
        notes: json['notes'] as String?,
        subtotal: (json['subtotal'] as num?)?.toDouble() ?? 0,
        taxRate: (json['taxRate'] as num?)?.toDouble() ?? 0,
        taxTotal: (json['taxTotal'] as num?)?.toDouble() ?? 0,
        total: (json['total'] as num).toDouble(),
        currency: json['currency'] as String? ?? 'HNL',
        validUntil: _date(json['validUntil']),
        respondedAt: _date(json['respondedAt']),
        customerResponseNote: json['customerResponseNote'] as String?,
        publicUrl: json['publicUrl'] as String?,
        isExpired: json['isExpired'] as bool? ?? false,
        lines: ((json['lines'] as List<dynamic>?) ?? [])
            .map((l) => QuoteLine.fromJson(l as Map<String, dynamic>))
            .toList(),
      );

  final String id;
  final String number;
  final QuoteStatus status;
  final String customerName;
  final String customerPhone;
  final String? vehicleLabel;
  final String? workOrderNumber;
  final String? notes;
  final double subtotal;
  final double taxRate;
  final double taxTotal;
  final double total;
  final String currency;
  final DateTime? validUntil;
  final DateTime? respondedAt;
  final String? customerResponseNote;

  /// Link que se comparte por WhatsApp. Null mientras sea un borrador.
  final String? publicUrl;
  final bool isExpired;
  final List<QuoteLine> lines;

  /// El cliente todavía puede responderla.
  bool get canRespond =>
      status == QuoteStatus.sent && respondedAt == null && !isExpired;

  static DateTime? _date(Object? value) =>
      value == null ? null : DateTime.parse(value as String);
}
