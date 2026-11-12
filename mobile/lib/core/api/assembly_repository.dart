import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/assembly.dart';

final assemblyRepositoryProvider = Provider<AssemblyRepository>(
  (ref) => AssemblyRepository(ref.watch(apiClientProvider).dio),
);

/// Las listas para registrar. Se cargan una vez al abrir la pantalla: son tres catálogos
/// cortos que casi nunca cambian.
final assemblyPickListsProvider = FutureProvider.autoDispose<AssemblyPickLists>(
  (ref) => ref.watch(assemblyRepositoryProvider).pickLists(),
);

/// Lo que lleva armado quien está dentro, con el filtro que esté mirando.
final myAssembliesProvider =
    FutureProvider.autoDispose.family<MyAssemblies, AssemblyFilter>(
  (ref, filtro) => ref.watch(assemblyRepositoryProvider).mine(filtro),
);

/// Las motos del taller con el filtro que se esté mirando. Del Dueño y del Encargado: al
/// técnico la API le responde 403, así que estas pantallas ni se le enseñan.
final assembliesProvider =
    FutureProvider.autoDispose.family<AssemblyPage, AssemblyFilter>(
  (ref, filtro) => ref.watch(assemblyRepositoryProvider).list(filtro),
);

/// Los cortes cerrados: las facturas a los contratistas y los pagos a los técnicos.
final assemblySettlementsProvider =
    FutureProvider.autoDispose.family<List<AssemblySettlement>, SettlementFilter>(
  (ref, filtro) => ref.watch(assemblyRepositoryProvider).settlements(filtro),
);

/// Quiénes han armado motos. Es la lista del filtro y la del corte de pago: incluye al
/// técnico que ya no está activo pero tiene motos sin pagar.
final assemblyTechniciansProvider = FutureProvider.autoDispose<List<AssemblyName>>(
  (ref) => ref.watch(assemblyRepositoryProvider).technicians(),
);

/// Lo que se le debe a cada técnico en el periodo que se esté mirando.
final assemblyPayReportProvider =
    FutureProvider.autoDispose.family<AssemblyPayReport, AssemblyFilter>(
  (ref, filtro) => ref.watch(assemblyRepositoryProvider).payReport(filtro),
);

final assemblyContractorsProvider =
    FutureProvider.autoDispose<List<AssemblyContractor>>(
  (ref) => ref.watch(assemblyRepositoryProvider).contractors(),
);

final assemblyModelsProvider = FutureProvider.autoDispose<List<AssemblyModel>>(
  (ref) => ref.watch(assemblyRepositoryProvider).models(),
);

final assemblyWarehousesProvider = FutureProvider.autoDispose<List<AssemblyWarehouse>>(
  (ref) => ref.watch(assemblyRepositoryProvider).warehouses(),
);

/// El gasto del taller por cada moto armada. Solo el Dueño.
final assemblyExpenseProvider = FutureProvider.autoDispose<double>(
  (ref) => ref.watch(assemblyRepositoryProvider).expensePerMotorcycle(),
);

/// Armado de motos.
class AssemblyRepository {
  AssemblyRepository(this._dio);

  final Dio _dio;

  Future<AssemblyPickLists> pickLists() async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/assembly/motorcycles/pick-lists',
    );

    return AssemblyPickLists.fromJson(response.data!);
  }

  Future<MotorcycleAssembly> register({
    required String serial,
    required String modelId,
    required String contractorId,
    required String warehouseId,
    String? notes,
    String? technicianUserId,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/assembly/motorcycles',
      data: {
        'serial': serial,
        'modelId': modelId,
        'contractorId': contractorId,
        'warehouseId': warehouseId,
        'notes': notes,
        // Vacío desde el técnico: el servidor se la apunta a quien está dentro. Lo manda
        // quien registra la moto que armó otro.
        if (technicianUserId != null) 'technicianUserId': technicianUserId,
      },
    );

    return MotorcycleAssembly.fromJson(response.data!);
  }

  /// Lo que lleva armado quien pregunta. Sin filtro es el mes corriente, que es lo que se
  /// quiere ver casi siempre; los que arman por tandas necesitan lo demás.
  Future<MyAssemblies> mine([AssemblyFilter filtro = const AssemblyFilter()]) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/assembly/motorcycles/mine',
      queryParameters: filtro.toQuery(),
    );

    return MyAssemblies.fromJson(response.data!);
  }

  /// Qué modelo suele llevar una serie como esa. Es una propuesta, nunca un relleno.
  Future<ModelGuess> guessModel(String serial) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/assembly/motorcycles/guess-model',
      queryParameters: {'serial': serial},
    );

    return ModelGuess.fromJson(response.data!);
  }

  // ---------- Lo del dueño ----------
  //
  // Cuelga de `/api/assembly/admin`, que es donde el servidor exige que mande quien
  // pregunta. Al técnico le responde 403 y al taller sin el módulo, 404.

  Future<AssemblyPage> list(AssemblyFilter filtro) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/assembly/admin/motorcycles',
      queryParameters: filtro.toQuery(),
    );

    return AssemblyPage.fromJson(response.data!);
  }

  /// Corregir una moto mal registrada.
  ///
  /// Con [charge] y [technicianPay] vacíos se recalculan de la tarifa de hoy, que es lo que
  /// se quiere cuando se registró con el modelo equivocado. Con número, mandan los números:
  /// es el trato que no sale de ninguna tabla.
  Future<AssemblyRow> correct(
    String id, {
    required String serial,
    required String modelId,
    required String contractorId,
    required String warehouseId,
    required String technicianUserId,
    required DateTime assembledOn,
    String? notes,
    double? charge,
    double? technicianPay,
  }) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/assembly/admin/motorcycles/$id',
      data: {
        'serial': serial,
        'modelId': modelId,
        'contractorId': contractorId,
        'warehouseId': warehouseId,
        'technicianUserId': technicianUserId,
        'assembledOn': diaApi(assembledOn),
        'notes': notes,
        if (charge != null) 'charge': charge,
        if (technicianPay != null) 'technicianPay': technicianPay,
      },
    );

    return AssemblyRow.fromJson(response.data!);
  }

  Future<void> remove(String id) =>
      _dio.delete<void>('/api/assembly/admin/motorcycles/$id');

  Future<List<AssemblyName>> technicians() async {
    final response = await _dio.get<List<dynamic>>('/api/assembly/admin/technicians');

    return response.data!
        .map((e) => AssemblyName.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<List<AssemblySettlement>> settlements([
    SettlementFilter filtro = const SettlementFilter(),
  ]) async {
    final response = await _dio.get<List<dynamic>>(
      '/api/assembly/admin/settlements',
      queryParameters: filtro.toQuery(),
    );

    return response.data!
        .map((e) => AssemblySettlement.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Cerrar un corte congela lo que incluyó: es lo que impide cobrar o pagar dos veces.
  ///
  /// [total] solo en un corte de pago, y solo cuando lo acordado no es la suma de sus motos.
  Future<AssemblySettlement> close({
    required AssemblySettlementKind kind,
    required DateTime from,
    required DateTime to,
    String? contractorId,
    String? technicianUserId,
    String? reference,
    double? total,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/assembly/admin/settlements',
      data: {
        'kind': kind.value,
        'from': diaApi(from),
        'to': diaApi(to),
        if (contractorId != null) 'contractorId': contractorId,
        if (technicianUserId != null) 'technicianUserId': technicianUserId,
        if (reference != null && reference.trim().isNotEmpty) 'reference': reference.trim(),
        if (total != null) 'total': total,
      },
    );

    return AssemblySettlement.fromJson(response.data!);
  }

  /// Anotar que el contratista pagó esa factura. Cerrar el corte era facturar, no cobrar.
  Future<AssemblySettlement> collect(
    String id, {
    DateTime? on,
    String? reference,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/assembly/admin/settlements/$id/collect',
      data: {
        if (on != null) 'on': diaApi(on),
        if (reference != null && reference.trim().isNotEmpty) 'reference': reference.trim(),
      },
    );

    return AssemblySettlement.fromJson(response.data!);
  }

  /// Quitarle el cobro: se anotó por error, o el cheque rebotó.
  Future<AssemblySettlement> uncollect(String id) async {
    final response = await _dio.delete<Map<String, dynamic>>(
      '/api/assembly/admin/settlements/$id/collect',
    );

    return AssemblySettlement.fromJson(response.data!);
  }

  /// Reabrirlo suelta las motos. Es la única manera honesta de corregir un corte.
  Future<void> reopen(String id) =>
      _dio.delete<void>('/api/assembly/admin/settlements/$id');

  Future<AssemblyPayReport> payReport(AssemblyFilter filtro) async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/assembly/admin/reports/pay',
      queryParameters: filtro.toQuery(),
    );

    return AssemblyPayReport.fromJson(response.data!);
  }

  /// La hoja de un corte ya cerrado, en PDF: la que se le manda al contratista para que
  /// pague, y la que se vuelve a sacar el día que discuta la factura.
  Future<List<int>> settlementPdf(String id) async {
    final response = await _dio.get<List<int>>(
      '/api/assembly/admin/settlements/$id/report.pdf',
      options: Options(responseType: ResponseType.bytes),
    );

    return response.data!;
  }

  // ---------- El catálogo del contrato ----------

  Future<List<AssemblyContractor>> contractors() async {
    final response = await _dio.get<List<dynamic>>('/api/assembly/contractors');

    return response.data!
        .map((e) => AssemblyContractor.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<AssemblyContractor> saveContractor({
    String? id,
    required String name,
    String? notes,
    bool isActive = true,
    String? branchId,
  }) async {
    final data = {
      'name': name,
      'notes': notes,
      'isActive': isActive,
      if (branchId != null) 'branchId': branchId,
    };

    final response = id == null
        ? await _dio.post<Map<String, dynamic>>('/api/assembly/contractors', data: data)
        : await _dio.put<Map<String, dynamic>>('/api/assembly/contractors/$id', data: data);

    return AssemblyContractor.fromJson(response.data!);
  }

  /// La tabla entera de un contratista, de una vez: se llena de un tirón al abrir el
  /// contrato y se repasa cuando se renegocia.
  Future<AssemblyContractor> saveRates(
    String contractorId,
    List<AssemblyRate> rates,
  ) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/assembly/contractors/$contractorId/rates',
      data: {
        'rates': [
          for (final r in rates)
            {
              'modelId': r.modelId,
              'charge': r.charge,
              'technicianPay': r.technicianPay,
            },
        ],
      },
    );

    return AssemblyContractor.fromJson(response.data!);
  }

  Future<List<AssemblyModel>> models() async {
    final response = await _dio.get<List<dynamic>>('/api/assembly/models');

    return response.data!
        .map((e) => AssemblyModel.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<AssemblyModel> saveModel({
    String? id,
    required String name,
    bool isActive = true,
    String? vinPrefix,
  }) async {
    final data = {'name': name, 'isActive': isActive, 'vinPrefix': vinPrefix};

    final response = id == null
        ? await _dio.post<Map<String, dynamic>>('/api/assembly/models', data: data)
        : await _dio.put<Map<String, dynamic>>('/api/assembly/models/$id', data: data);

    return AssemblyModel.fromJson(response.data!);
  }

  Future<List<AssemblyWarehouse>> warehouses() async {
    final response = await _dio.get<List<dynamic>>('/api/assembly/warehouses');

    return response.data!
        .map((e) => AssemblyWarehouse.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<AssemblyWarehouse> saveWarehouse({
    String? id,
    required String name,
    bool isActive = true,
  }) async {
    final data = {'name': name, 'isActive': isActive};

    final response = id == null
        ? await _dio.post<Map<String, dynamic>>('/api/assembly/warehouses', data: data)
        : await _dio.put<Map<String, dynamic>>('/api/assembly/warehouses/$id', data: data);

    return AssemblyWarehouse.fromJson(response.data!);
  }

  Future<double> expensePerMotorcycle() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/assembly/settings');

    return (response.data!['expensePerMotorcycle'] as num).toDouble();
  }

  Future<double> saveExpensePerMotorcycle(double value) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/assembly/settings',
      data: {'expensePerMotorcycle': value},
    );

    return (response.data!['expensePerMotorcycle'] as num).toDouble();
  }
}
