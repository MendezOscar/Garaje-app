import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';
import '../models/work_steps_proposal.dart';

final workStepsRepositoryProvider = Provider<WorkStepsRepository>(
  (ref) => WorkStepsRepository(ref.watch(apiClientProvider).dio),
);

/// «Proponer los pasos»: la misma propuesta que el panel, desde el teléfono.
class WorkStepsRepository {
  WorkStepsRepository(this._dio);

  final Dio _dio;

  /// Pide la propuesta. **No escribe en la orden**: lo que vuelve es un borrador.
  Future<WorkStepsProposal> propose(String workOrderId) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/assistant/steps',
      data: const <String, dynamic>{},
    );

    return WorkStepsProposal.fromJson(response.data!);
  }

  /// Crea los pasos que quedaron después de editar. Los repuestos vuelven como sugerencia:
  /// cargarlos descontaría la bodega y el trabajo todavía no se ha hecho.
  Future<void> accept(
    String workOrderId,
    String proposalId,
    List<ProposedTask> tasks,
  ) async {
    await _dio.post<Map<String, dynamic>>(
      '/api/work-orders/$workOrderId/assistant/steps/$proposalId/accept',
      data: {'tasks': tasks.map((t) => t.toJson()).toList()},
    );
  }
}
