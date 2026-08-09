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
  Future<List<WorkOrderListItem>> list({bool onlyOpen = true}) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/work-orders',
      queryParameters: {'onlyOpen': onlyOpen, 'pageSize': 100},
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
    double? laborPrice,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/tasks',
      data: {
        'title': title,
        'laborServiceId': laborServiceId,
        'laborPrice': laborPrice,
      },
    );
    return WorkOrderTask.fromJson(response.data!);
  }

  /// Le pone (o le quita) precio a un paso, del catálogo o a mano. Un paso sin precio no se
  /// factura. Lo que no se manda se borra: elegir servicio limpia el precio a mano, y poner
  /// precio a mano deja el servicio como estaba.
  Future<WorkOrderTask> setTaskLabor(
    String workOrderId,
    WorkOrderTask task, {
    String? laborServiceId,
    double? laborPrice,
  }) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/tasks/${task.id}',
      // Las horas van sin mandar a propósito: al cambiar de servicio el backend vuelve a
      // poner las estándar del nuevo, que es lo que se quiere cobrar.
      data: {
        'title': task.title,
        'description': task.description,
        'laborServiceId': laborServiceId,
        'laborPrice': laborPrice,
      },
    );
    return WorkOrderTask.fromJson(response.data!);
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

/// Lista de órdenes del usuario. `autoDispose` para que al volver a la pantalla se recargue:
/// el técnico necesita ver lo que el dueño le asignó mientras estaba en otra vista.
final myWorkOrdersProvider = FutureProvider.autoDispose<List<WorkOrderListItem>>(
  (ref) => ref.watch(workOrderRepositoryProvider).list(),
);

final workOrderDetailProvider =
    FutureProvider.autoDispose.family<WorkOrderDetail, String>(
  (ref, id) => ref.watch(workOrderRepositoryProvider).get(id),
);
