// Espejo de los DTOs de la Fase 1. Los valores numéricos coinciden con Garaj.Domain.Enums.

import 'inventory.dart';

enum WorkOrderStatus {
  received(1, 'Recibida'),
  diagnosing(2, 'En diagnóstico'),
  waitingApproval(3, 'Esperando aprobación'),
  waitingParts(4, 'Esperando repuestos'),
  inProgress(5, 'En proceso'),
  testing(6, 'En pruebas'),
  ready(7, 'Lista para entrega'),
  delivered(8, 'Entregada'),
  cancelled(9, 'Cancelada');

  const WorkOrderStatus(this.value, this.label);

  final int value;
  final String label;

  static WorkOrderStatus fromValue(int value) =>
      WorkOrderStatus.values.firstWhere((s) => s.value == value,
          orElse: () => WorkOrderStatus.received);

  /// La orden sigue viva en el taller.
  bool get isOpen => this != WorkOrderStatus.delivered && this != WorkOrderStatus.cancelled;

  /// Detenida esperando a alguien de fuera del taller.
  bool get isBlocked =>
      this == WorkOrderStatus.waitingApproval || this == WorkOrderStatus.waitingParts;
}

enum VehicleType {
  car(1, 'Vehículo'),
  motorcycle(2, 'Moto');

  const VehicleType(this.value, this.label);

  final int value;
  final String label;

  static VehicleType fromValue(int value) =>
      VehicleType.values.firstWhere((t) => t.value == value, orElse: () => VehicleType.car);
}

class WorkOrderListItem {
  const WorkOrderListItem({
    required this.id,
    required this.number,
    required this.branchName,
    required this.status,
    required this.vehicleLabel,
    required this.vehicleType,
    required this.customerName,
    required this.customerPhone,
    required this.description,
    required this.openedAt,
    required this.taskCount,
    required this.tasksDone,
    this.plate,
    this.promisedAt,
    this.assignedTechnicianName,
  });

  factory WorkOrderListItem.fromJson(Map<String, dynamic> json) => WorkOrderListItem(
        id: json['id'] as String,
        number: json['number'] as String,
        branchName: json['branchName'] as String,
        status: WorkOrderStatus.fromValue(json['status'] as int),
        vehicleLabel: json['vehicleLabel'] as String,
        vehicleType: VehicleType.fromValue(json['vehicleType'] as int),
        plate: json['plate'] as String?,
        customerName: json['customerName'] as String,
        customerPhone: json['customerPhone'] as String,
        assignedTechnicianName: json['assignedTechnicianName'] as String?,
        description: json['description'] as String,
        openedAt: DateTime.parse(json['openedAt'] as String),
        promisedAt: json['promisedAt'] == null
            ? null
            : DateTime.parse(json['promisedAt'] as String),
        taskCount: json['taskCount'] as int,
        tasksDone: json['tasksDone'] as int,
      );

  final String id;
  final String number;
  final String branchName;
  final WorkOrderStatus status;
  final String vehicleLabel;
  final VehicleType vehicleType;
  final String? plate;
  final String customerName;
  final String customerPhone;
  final String? assignedTechnicianName;
  final String description;
  final DateTime openedAt;
  final DateTime? promisedAt;
  final int taskCount;
  final int tasksDone;

  /// Pasó la fecha comprometida con el cliente y la orden sigue abierta.
  bool get isLate =>
      promisedAt != null && promisedAt!.isBefore(DateTime.now()) && status.isOpen;
}

class WorkOrderTask {
  const WorkOrderTask({
    required this.id,
    required this.title,
    required this.sequence,
    required this.isDone,
    this.description,
    this.assignedTechnicianName,
    this.estimatedHours,
    this.actualHours,
    this.technicianNotes,
  });

  factory WorkOrderTask.fromJson(Map<String, dynamic> json) => WorkOrderTask(
        id: json['id'] as String,
        title: json['title'] as String,
        description: json['description'] as String?,
        sequence: json['sequence'] as int,
        isDone: json['isDone'] as bool,
        assignedTechnicianName: json['assignedTechnicianName'] as String?,
        estimatedHours: (json['estimatedHours'] as num?)?.toDouble(),
        actualHours: (json['actualHours'] as num?)?.toDouble(),
        technicianNotes: json['technicianNotes'] as String?,
      );

  final String id;
  final String title;
  final String? description;
  final int sequence;
  final bool isDone;
  final String? assignedTechnicianName;
  final double? estimatedHours;
  final double? actualHours;
  final String? technicianNotes;
}

class WorkOrderStatusEntry {
  const WorkOrderStatusEntry({
    required this.toStatus,
    required this.changedAt,
    required this.changedByName,
    required this.isVisibleToCustomer,
    this.note,
  });

  factory WorkOrderStatusEntry.fromJson(Map<String, dynamic> json) => WorkOrderStatusEntry(
        toStatus: WorkOrderStatus.fromValue(json['toStatus'] as int),
        changedAt: DateTime.parse(json['changedAt'] as String),
        changedByName: json['changedByName'] as String,
        note: json['note'] as String?,
        isVisibleToCustomer: json['isVisibleToCustomer'] as bool,
      );

  final WorkOrderStatus toStatus;
  final DateTime changedAt;
  final String changedByName;
  final String? note;
  final bool isVisibleToCustomer;
}

class WorkOrderDetail {
  const WorkOrderDetail({
    required this.id,
    required this.number,
    required this.branchName,
    required this.status,
    required this.allowedNextStatuses,
    required this.vehicleLabel,
    required this.vehicleType,
    required this.customerName,
    required this.customerPhone,
    required this.description,
    required this.openedAt,
    required this.tasks,
    required this.timeline,
    required this.parts,
    required this.partsTotal,
    this.plate,
    this.diagnosis,
    this.mileageIn,
    this.promisedAt,
    this.assignedTechnicianName,
  });

  factory WorkOrderDetail.fromJson(Map<String, dynamic> json) => WorkOrderDetail(
        id: json['id'] as String,
        number: json['number'] as String,
        branchName: json['branchName'] as String,
        status: WorkOrderStatus.fromValue(json['status'] as int),
        allowedNextStatuses: (json['allowedNextStatuses'] as List<dynamic>)
            .map((v) => WorkOrderStatus.fromValue(v as int))
            .toList(),
        vehicleLabel: json['vehicleLabel'] as String,
        vehicleType: VehicleType.fromValue(json['vehicleType'] as int),
        plate: json['plate'] as String?,
        customerName: json['customerName'] as String,
        customerPhone: json['customerPhone'] as String,
        assignedTechnicianName: json['assignedTechnicianName'] as String?,
        description: json['description'] as String,
        diagnosis: json['diagnosis'] as String?,
        mileageIn: json['mileageIn'] as int?,
        openedAt: DateTime.parse(json['openedAt'] as String),
        promisedAt: json['promisedAt'] == null
            ? null
            : DateTime.parse(json['promisedAt'] as String),
        tasks: (json['tasks'] as List<dynamic>)
            .map((t) => WorkOrderTask.fromJson(t as Map<String, dynamic>))
            .toList(),
        timeline: (json['timeline'] as List<dynamic>)
            .map((t) => WorkOrderStatusEntry.fromJson(t as Map<String, dynamic>))
            .toList(),
        parts: (json['parts'] as List<dynamic>)
            .map((p) => WorkOrderPart.fromJson(p as Map<String, dynamic>))
            .toList(),
        partsTotal: (json['partsTotal'] as num).toDouble(),
      );

  final String id;
  final String number;
  final String branchName;
  final WorkOrderStatus status;
  final List<WorkOrderStatus> allowedNextStatuses;
  final String vehicleLabel;
  final VehicleType vehicleType;
  final String? plate;
  final String customerName;
  final String customerPhone;
  final String? assignedTechnicianName;
  final String description;
  final String? diagnosis;
  final int? mileageIn;
  final DateTime openedAt;
  final DateTime? promisedAt;
  final List<WorkOrderTask> tasks;
  final List<WorkOrderStatusEntry> timeline;
  final List<WorkOrderPart> parts;

  /// Lo que suman los repuestos consumidos. La mano de obra entra en la Fase 4.
  final double partsTotal;
}
