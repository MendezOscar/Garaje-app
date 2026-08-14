import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/work_order.dart';

final workOrderRepositoryProvider = Provider<WorkOrderRepository>(
  (ref) => WorkOrderRepository(ref.watch(apiClientProvider).dio),
);

class WorkOrderRepository {
  WorkOrderRepository(this._dio);

  final Dio _dio;

  /// El backend ya limita el resultado a lo que corresponde al perfil: el Técnico recibe
  /// solo sus asignaciones y el Cliente solo las órdenes de sus vehículos.
  Future<List<WorkOrderListItem>> list({
    bool onlyOpen = true,
    String? vehicleId,
    String? search,
  }) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/work-orders',
      queryParameters: {
        'onlyOpen': onlyOpen,
        'pageSize': 100,
        if (vehicleId != null) 'vehicleId': vehicleId,
        // El backend busca por número, por placa normalizada y por nombre del cliente.
        if (search != null && search.trim().isNotEmpty) 'search': search.trim(),
      },
    );

    return (response.data!['items'] as List<dynamic>)
        .map((e) => WorkOrderListItem.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<WorkOrderDetail> get(String id) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/work-orders/$id');
    return WorkOrderDetail.fromJson(response.data!);
  }

  Future<WorkOrderDetail> changeStatus(
    String id,
    WorkOrderStatus status, {
    String? note,
    bool isVisibleToCustomer = true,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$id/status',
      data: {
        'status': status.value,
        'note': note,
        'isVisibleToCustomer': isVisibleToCustomer,
      },
    );
    return WorkOrderDetail.fromJson(response.data!);
  }

  Future<WorkOrderTask> completeTask(
    String workOrderId,
    String taskId, {
    required bool isDone,
    double? actualHours,
    String? technicianNotes,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/tasks/$taskId/complete',
      data: {
        'isDone': isDone,
        'actualHours': actualHours,
        'technicianNotes': technicianNotes,
      },
    );
    return WorkOrderTask.fromJson(response.data!);
  }

  /// Asigna —o quita— el técnico responsable. Solo el Dueño; la API rechaza a un técnico
  /// que no trabaje en la sucursal de la orden.
  Future<WorkOrderDetail> assign(String id, String? technicianId) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/work-orders/$id/assign',
      data: {'technicianId': technicianId},
    );
    return WorkOrderDetail.fromJson(response.data!);
  }

  /// Guarda el diagnóstico: lo que el técnico encontró al revisar el vehículo.
  ///
  /// El motivo de ingreso y la fecha prometida se reenvían tal cual porque el endpoint
  /// reemplaza la orden entera: omitirlos borraría el compromiso con el cliente.
  Future<WorkOrderDetail> saveDiagnosis(
    String id, {
    required String description,
    required String? diagnosis,
    DateTime? promisedAt,
  }) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/work-orders/$id',
      data: {
        'description': description,
        'diagnosis': diagnosis,
        'promisedAt': promisedAt?.toUtc().toIso8601String(),
      },
    );
    return WorkOrderDetail.fromJson(response.data!);
  }

  Future<WorkOrderTask> addTask(
    String workOrderId,
    String title, {
    String? laborServiceId,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/tasks',
      data: {'title': title, 'laborServiceId': laborServiceId},
    );
    return WorkOrderTask.fromJson(response.data!);
  }

  /// Le pone (o le quita) el servicio del catálogo que da precio al paso. Solo cuenta en las
  /// órdenes en modo catálogo: en las manuales el precio es uno solo para toda la orden.
  Future<WorkOrderTask> setTaskLabor(
    String workOrderId,
    WorkOrderTask task, {
    String? laborServiceId,
  }) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/tasks/${task.id}',
      // Las horas van sin mandar a propósito: al cambiar de servicio el backend vuelve a
      // poner las estándar del nuevo, que es lo que se quiere cobrar.
      data: {
        'title': task.title,
        'description': task.description,
        'laborServiceId': laborServiceId,
      },
    );
    return WorkOrderTask.fromJson(response.data!);
  }

  /// Elige si la mano de obra sale del catálogo o de un total escrito a mano. Solo el Dueño:
  /// decide lo que se le cobra al cliente.
  Future<WorkOrderDetail> setLaborMode(
    String id,
    LaborMode mode, {
    double? total,
  }) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/work-orders/$id/labor',
      data: {'mode': mode.value, 'total': total},
    );
    return WorkOrderDetail.fromJson(response.data!);
  }

  /// El link `wa.me` con el mensaje ya escrito y el enlace de seguimiento dentro.
  ///
  /// `kind` es `received` al recibir el vehículo, `ready` cuando está listo o `invoice` para
  /// mandar la factura, que falla con 400 si la orden todavía no se ha cerrado.
  Future<String> trackingLink(String id, String kind) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/work-orders/$id/whatsapp',
      queryParameters: {'kind': kind},
    );
    return response.data!['url'] as String;
  }

  /// Los vehículos a los que les toca servicio. Solo el Dueño: a los demás la API dice 403.
  Future<List<ServiceReminder>> serviceReminders({
    bool? overdue,
    bool includeReminded = false,
    String? search,
    int withinDays = 30,
  }) async {
    final response = await _dio.get<List<dynamic>>(
      '/api/work-orders/reminders',
      queryParameters: {
        'withinDays': withinDays,
        if (overdue != null) 'overdue': overdue,
        if (includeReminded) 'includeReminded': true,
        if (search != null && search.isNotEmpty) 'search': search,
      },
    );

    return response.data!
        .map((e) => ServiceReminder.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Arma el recordatorio y deja constancia de que ya se le avisó.
  Future<String> serviceReminderLink(String workOrderId) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/service-reminder',
    );
    return response.data!['url'] as String;
  }

  /// El catálogo de mano de obra del taller. El backend se lo niega al Cliente.
  Future<List<LaborServiceOption>> laborServices() async {
    final response = await _dio.get<List<dynamic>>('/api/labor-services');
    return response.data!
        .map((e) => LaborServiceOption.fromJson(e as Map<String, dynamic>))
        .toList();
  }
}

/// Un servicio del catálogo, con el precio ya resuelto por el backend.
class LaborServiceOption {
  const LaborServiceOption({required this.id, required this.name, required this.price});

  factory LaborServiceOption.fromJson(Map<String, dynamic> json) => LaborServiceOption(
        id: json['id'] as String,
        name: json['name'] as String,
        price: (json['price'] as num).toDouble(),
      );

  final String id;
  final String name;
  final double price;
}

/// El catálogo cambia poco: se pide una vez y se comparte entre pantallas.
final laborServicesProvider = FutureProvider<List<LaborServiceOption>>(
  (ref) => ref.watch(workOrderRepositoryProvider).laborServices(),
);

/// Un vehículo al que le toca servicio, según lo que el taller anotó al entregarlo.
class ServiceReminder {
  const ServiceReminder({
    required this.workOrderId,
    required this.orderNumber,
    required this.customerName,
    required this.customerPhone,
    required this.vehicleLabel,
    required this.plate,
    required this.branchName,
    required this.lastService,
    required this.nextServiceAt,
    required this.daysUntil,
    required this.nextServiceMileage,
    required this.lastMileage,
    required this.remindedAt,
  });

  factory ServiceReminder.fromJson(Map<String, dynamic> json) => ServiceReminder(
        workOrderId: json['workOrderId'] as String,
        orderNumber: json['orderNumber'] as String,
        customerName: json['customerName'] as String,
        customerPhone: json['customerPhone'] as String,
        vehicleLabel: json['vehicleLabel'] as String,
        plate: json['plate'] as String?,
        branchName: json['branchName'] as String,
        lastService: json['lastService'] as String,
        nextServiceAt: DateTime.parse(json['nextServiceAt'] as String),
        daysUntil: json['daysUntil'] as int,
        nextServiceMileage: json['nextServiceMileage'] as int?,
        lastMileage: json['lastMileage'] as int?,
        remindedAt: json['remindedAt'] == null
            ? null
            : DateTime.parse(json['remindedAt'] as String),
      );

  final String workOrderId;
  final String orderNumber;
  final String customerName;
  final String customerPhone;
  final String vehicleLabel;
  final String? plate;
  final String branchName;

  /// Qué se le hizo la última vez. Da de qué hablar al llamarlo.
  final String lastService;
  final DateTime nextServiceAt;

  /// Días hasta que toque. Negativo si ya pasó.
  final int daysUntil;
  final int? nextServiceMileage;
  final int? lastMileage;
  final DateTime? remindedAt;

  bool get isOverdue => daysUntil < 0;
}

/// Qué recordatorios se están mirando.
enum ReminderFilter {
  /// Los del mes, sin los que ya se avisaron.
  month,

  /// Solo los que ya se pasaron de fecha.
  overdue,

  /// Los que ya se recordaron, para no volver a llamar.
  reminded,
}

final serviceRemindersProvider =
    FutureProvider.autoDispose.family<List<ServiceReminder>, ReminderFilter>(
  (ref, filter) => ref.watch(workOrderRepositoryProvider).serviceReminders(
        overdue: filter == ReminderFilter.overdue ? true : null,
        includeReminded: filter == ReminderFilter.reminded,
        // Los ya recordados se buscan hacia atrás, sin recortar por fecha.
        withinDays: filter == ReminderFilter.reminded ? 365 : 30,
        search: ref.watch(remindersSearchProvider),
      ),
);

/// Lo escrito en el buscador de recordatorios. Fuera de la pantalla para que sobreviva a ir
/// al detalle de una orden y volver.
final remindersSearchProvider =
    NotifierProvider<RemindersSearch, String>(RemindersSearch.new);

class RemindersSearch extends Notifier<String> {
  @override
  String build() => '';

  void set(String value) => state = value;
}

/// Si la bandeja muestra solo las órdenes vivas o también las entregadas y canceladas.
/// Arranca en `true`: lo del día es lo que interesa al abrir la aplicación.
final onlyOpenOrdersProvider =
    NotifierProvider<OnlyOpenOrders, bool>(OnlyOpenOrders.new);

class OnlyOpenOrders extends Notifier<bool> {
  @override
  bool build() => true;

  void set(bool value) => state = value;
}

/// Lo que se escribió en el buscador de la bandeja. Vive fuera de la pantalla para que la
/// búsqueda sobreviva a ir al detalle de una orden y volver.
final ordersSearchProvider = NotifierProvider<OrdersSearch, String>(OrdersSearch.new);

class OrdersSearch extends Notifier<String> {
  @override
  String build() => '';

  void set(String value) => state = value;
}

/// Lista de órdenes del usuario. `autoDispose` para que al volver a la pantalla se recargue:
/// el técnico necesita ver lo que el dueño le asignó mientras estaba en otra vista.
final myWorkOrdersProvider = FutureProvider.autoDispose<List<WorkOrderListItem>>(
  (ref) {
    final search = ref.watch(ordersSearchProvider);

    return ref.watch(workOrderRepositoryProvider).list(
          // Buscando se deja de filtrar: quien teclea una placa la quiere encontrar aunque
          // el vehículo ya haya salido del taller.
          onlyOpen: search.trim().isEmpty && ref.watch(onlyOpenOrdersProvider),
          search: search,
        );
  },
);

/// Todo lo que se le ha hecho a un vehículo, entregado incluido. Es lo que se pregunta en el
/// mostrador cuando el cliente vuelve: «¿qué le hicieron la vez pasada?».
final vehicleHistoryProvider =
    FutureProvider.autoDispose.family<List<WorkOrderListItem>, String>(
  (ref, vehicleId) => ref
      .watch(workOrderRepositoryProvider)
      .list(onlyOpen: false, vehicleId: vehicleId),
);

final workOrderDetailProvider =
    FutureProvider.autoDispose.family<WorkOrderDetail, String>(
  (ref, id) => ref.watch(workOrderRepositoryProvider).get(id),
);
