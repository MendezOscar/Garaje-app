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

/// Lo que lleva armado quien está dentro, en el mes corriente.
final myAssembliesProvider = FutureProvider.autoDispose<MyAssemblies>(
  (ref) => ref.watch(assemblyRepositoryProvider).mine(),
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
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/assembly/motorcycles',
      data: {
        'serial': serial,
        'modelId': modelId,
        'contractorId': contractorId,
        'warehouseId': warehouseId,
        'notes': notes,
      },
    );

    return MotorcycleAssembly.fromJson(response.data!);
  }

  Future<MyAssemblies> mine() async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/assembly/motorcycles/mine',
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
}
