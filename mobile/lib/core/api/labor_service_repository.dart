import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// El catálogo de mano de obra: lo que el taller cobra por cada trabajo.
///
/// Se administra desde el teléfono porque el precio se decide donde se discute —con el cliente
/// delante o al terminar el trabajo— y no sentado frente a una computadora.
class LaborService {
  const LaborService({
    required this.id,
    required this.code,
    required this.name,
    required this.standardHours,
    required this.hourlyRate,
    required this.isFixedPrice,
    required this.fixedPrice,
    required this.isActive,
    required this.price,
    this.description,
    this.category,
  });

  factory LaborService.fromJson(Map<String, dynamic> json) => LaborService(
        id: json['id'] as String,
        code: json['code'] as String,
        name: json['name'] as String,
        description: json['description'] as String?,
        category: json['category'] as String?,
        standardHours: (json['standardHours'] as num?)?.toDouble() ?? 0,
        hourlyRate: (json['hourlyRate'] as num?)?.toDouble() ?? 0,
        isFixedPrice: json['isFixedPrice'] as bool? ?? false,
        fixedPrice: (json['fixedPrice'] as num?)?.toDouble() ?? 0,
        isActive: json['isActive'] as bool? ?? true,
        price: (json['price'] as num?)?.toDouble() ?? 0,
      );

  final String id;
  final String code;
  final String name;
  final String? description;
  final String? category;

  /// Las horas que el trabajo debería tomar. Con la tarifa por hora dan el precio.
  final double standardHours;
  final double hourlyRate;

  /// Precio cerrado: se cobra lo mismo tarde lo que tarde.
  final bool isFixedPrice;
  final double fixedPrice;
  final bool isActive;

  /// Lo que se cobraría por una unidad, ya resuelto por el servidor.
  final double price;
}

class LaborServiceRepository {
  LaborServiceRepository(this._dio);

  final Dio _dio;

  Future<List<LaborService>> list({bool includeInactive = true}) async {
    final response = await _dio.get<List<dynamic>>(
      '/api/labor-services',
      queryParameters: {'includeInactive': includeInactive},
    );

    return (response.data ?? [])
        .map((e) => LaborService.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// Crea o corrige. Con [id] corrige; sin él, crea.
  Future<LaborService> save({
    String? id,
    required String code,
    required String name,
    String? description,
    String? category,
    required double standardHours,
    required double hourlyRate,
    required bool isFixedPrice,
    required double fixedPrice,
    bool isActive = true,
  }) async {
    final data = {
      'code': code,
      'name': name,
      'description': description,
      'category': category,
      'standardHours': standardHours,
      'hourlyRate': hourlyRate,
      'isFixedPrice': isFixedPrice,
      'fixedPrice': fixedPrice,
      'isActive': isActive,
    };

    final response = id == null
        ? await _dio.post<Map<String, dynamic>>('/api/labor-services', data: data)
        : await _dio.put<Map<String, dynamic>>('/api/labor-services/$id', data: data);

    return LaborService.fromJson(response.data!);
  }
}

final laborServiceRepositoryProvider = Provider<LaborServiceRepository>(
  (ref) => LaborServiceRepository(ref.watch(apiClientProvider).dio),
);

/// El catálogo completo, los desactivados incluidos: esta pantalla los administra.
final laborCatalogProvider = FutureProvider.autoDispose<List<LaborService>>(
  (ref) => ref.watch(laborServiceRepositoryProvider).list(),
);

/// Crea un servicio de mano de obra con precio fijo y devuelve su id.
///
/// El código se arma solo —tres letras del nombre y un número— porque pedirlo en el teléfono
/// es una pregunta que nadie sabe contestar. Si ya existe, se prueba el siguiente: el servidor
/// exige que no se repita.
///
/// Vive aquí y no en una pantalla porque lo usan dos: el paso de una orden y el paso de un
/// trabajo frecuente, y el código generado tiene que salir igual en los dos.
Future<String> crearServicioDeManoDeObra(
  WidgetRef ref,
  String nombre,
  double precio,
) async {
  final letras = nombre
      .toUpperCase()
      .replaceAll(RegExp(r'[^A-Z0-9]'), '')
      .padRight(3, 'X')
      .substring(0, 3);

  final repo = ref.read(laborServiceRepositoryProvider);

  for (var n = 1; n <= 20; n++) {
    try {
      final creado = await repo.save(
        code: '$letras-${n.toString().padLeft(2, '0')}',
        name: nombre,
        standardHours: 0,
        hourlyRate: 0,
        isFixedPrice: true,
        fixedPrice: precio,
      );

      return creado.id;
    } on DioException catch (e) {
      // 409 es «ese código ya está usado»: se prueba con el siguiente número.
      if (e.response?.statusCode != 409) rethrow;
    }
  }

  throw Exception('No se pudo crear el servicio: todos los códigos probados están usados.');
}
