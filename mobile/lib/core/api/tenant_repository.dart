import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/auth_controller.dart';

/// Rango de facturación autorizado por el SAR, visto desde el teléfono.
///
/// La app no registra rangos —eso se hace una vez, desde el panel— pero sí necesita saber si
/// la sucursal puede emitir con CAI antes de ofrecer la casilla al facturar.
class FiscalRange {
  const FiscalRange({
    required this.id,
    required this.branchId,
    required this.nextFiscalNumber,
    required this.remaining,
    required this.isActive,
    required this.isExpired,
    required this.isExhausted,
  });

  factory FiscalRange.fromJson(Map<String, dynamic> json) => FiscalRange(
        id: json['id'] as String,
        branchId: json['branchId'] as String,
        nextFiscalNumber: json['nextFiscalNumber'] as String,
        remaining: json['remaining'] as int,
        isActive: json['isActive'] as bool,
        isExpired: json['isExpired'] as bool,
        isExhausted: json['isExhausted'] as bool,
      );

  final String id;
  final String branchId;
  final String nextFiscalNumber;
  final int remaining;
  final bool isActive;
  final bool isExpired;
  final bool isExhausted;

  /// Puede emitir: está activo, no venció y le quedan números.
  bool get canIssue => isActive && !isExpired && !isExhausted;
}

class TenantRepository {
  TenantRepository(this._dio);

  final Dio _dio;

  /// El ISV que el taller aplica al facturar. Sin él no se puede decir cuánto va a pagar el
  /// cliente: el total de la orden sin impuesto no es lo que sale en la factura.
  Future<double> defaultTaxRate() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/tenant');
    return ((response.data ?? const {})['defaultTaxRate'] as num?)?.toDouble() ?? 0;
  }

  /// Los días de garantía que el taller da por defecto. Cero es sin garantía.
  Future<int> defaultWarrantyDays() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/tenant');
    return ((response.data ?? const {})['defaultWarrantyDays'] as num?)?.toInt() ?? 0;
  }

  /// Los ajustes del taller, completos. Es el mismo `/api/tenant` que ya se consulta para el
  /// ISV y la garantía; aquí viene todo porque la pantalla de ajustes los edita juntos.
  Future<TenantSettings> settings() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/tenant');
    return TenantSettings.fromJson(response.data ?? const {});
  }

  /// Guarda los ajustes. El servidor reemplaza el taller completo con lo que reciba, así que
  /// se manda todo —también lo que no se tocó—, igual que hace el panel.
  Future<TenantSettings> saveSettings(TenantSettings s) async {
    final response = await _dio.put<Map<String, dynamic>>('/api/tenant', data: s.toJson());
    return TenantSettings.fromJson(response.data ?? const {});
  }

  Future<List<FiscalRange>> fiscalRanges() async {
    final response = await _dio.get<List<dynamic>>('/api/tenant/fiscal-ranges');

    return (response.data ?? [])
        .map((e) => FiscalRange.fromJson(e as Map<String, dynamic>))
        .toList();
  }
}

/// Los ajustes del taller: lo que se imprime, lo que se cobra y lo que el técnico ve.
class TenantSettings {
  const TenantSettings({
    required this.name,
    this.legalName,
    this.taxId,
    this.phone,
    this.email,
    this.address,
    required this.defaultTaxRate,
    this.defaultPhoneCountryCode,
    required this.defaultWarrantyDays,
    required this.chargesStorage,
    required this.storageFreeDays,
    required this.storageDailyRate,
    required this.techniciansSeePrices,
  });

  factory TenantSettings.fromJson(Map<String, dynamic> json) => TenantSettings(
        name: json['name'] as String? ?? '',
        legalName: json['legalName'] as String?,
        taxId: json['taxId'] as String?,
        phone: json['phone'] as String?,
        email: json['email'] as String?,
        address: json['address'] as String?,
        defaultTaxRate: (json['defaultTaxRate'] as num?)?.toDouble() ?? 0,
        defaultPhoneCountryCode: json['defaultPhoneCountryCode'] as String?,
        defaultWarrantyDays: (json['defaultWarrantyDays'] as num?)?.toInt() ?? 0,
        chargesStorage: json['chargesStorage'] as bool? ?? false,
        storageFreeDays: (json['storageFreeDays'] as num?)?.toInt() ?? 3,
        storageDailyRate: (json['storageDailyRate'] as num?)?.toDouble() ?? 0,
        techniciansSeePrices: json['techniciansSeePrices'] as bool? ?? true,
      );

  final String name;
  final String? legalName;
  final String? taxId;
  final String? phone;
  final String? email;
  final String? address;
  final double defaultTaxRate;
  final String? defaultPhoneCountryCode;
  final int defaultWarrantyDays;
  final bool chargesStorage;
  final int storageFreeDays;
  final double storageDailyRate;
  final bool techniciansSeePrices;

  Map<String, dynamic> toJson() => {
        'name': name,
        'legalName': legalName,
        'taxId': taxId,
        'phone': phone,
        'email': email,
        'address': address,
        'defaultTaxRate': defaultTaxRate,
        'defaultPhoneCountryCode': defaultPhoneCountryCode,
        'defaultWarrantyDays': defaultWarrantyDays,
        'chargesStorage': chargesStorage,
        'storageFreeDays': storageFreeDays,
        'storageDailyRate': storageDailyRate,
        'techniciansSeePrices': techniciansSeePrices,
      };

  TenantSettings copyWith({
    String? name,
    String? legalName,
    String? taxId,
    String? phone,
    String? email,
    String? address,
    double? defaultTaxRate,
    String? defaultPhoneCountryCode,
    int? defaultWarrantyDays,
    bool? chargesStorage,
    int? storageFreeDays,
    double? storageDailyRate,
    bool? techniciansSeePrices,
  }) =>
      TenantSettings(
        name: name ?? this.name,
        legalName: legalName ?? this.legalName,
        taxId: taxId ?? this.taxId,
        phone: phone ?? this.phone,
        email: email ?? this.email,
        address: address ?? this.address,
        defaultTaxRate: defaultTaxRate ?? this.defaultTaxRate,
        defaultPhoneCountryCode: defaultPhoneCountryCode ?? this.defaultPhoneCountryCode,
        defaultWarrantyDays: defaultWarrantyDays ?? this.defaultWarrantyDays,
        chargesStorage: chargesStorage ?? this.chargesStorage,
        storageFreeDays: storageFreeDays ?? this.storageFreeDays,
        storageDailyRate: storageDailyRate ?? this.storageDailyRate,
        techniciansSeePrices: techniciansSeePrices ?? this.techniciansSeePrices,
      );
}

final tenantSettingsProvider = FutureProvider.autoDispose<TenantSettings>(
  (ref) => ref.watch(tenantRepositoryProvider).settings(),
);

final tenantRepositoryProvider = Provider<TenantRepository>(
  (ref) => TenantRepository(ref.watch(apiClientProvider).dio),
);

/// El ISV del taller. Solo el Dueño lo puede consultar —la ficha del taller es suya—, así que
/// el error se traga y devuelve cero: para el Técnico no hay total con impuesto que enseñar.
final taxRateProvider = FutureProvider<double>((ref) async {
  try {
    return await ref.watch(tenantRepositoryProvider).defaultTaxRate();
  } catch (_) {
    return 0;
  }
});

/// La garantía por defecto del taller. Se traga el error igual que el ISV: para quien no es
/// Dueño, la ficha del taller no se consulta y la venta sale sin garantía.
final defaultWarrantyDaysProvider = FutureProvider<int>((ref) async {
  try {
    return await ref.watch(tenantRepositoryProvider).defaultWarrantyDays();
  } catch (_) {
    return 0;
  }
});

/// El rango vigente de una sucursal, o null si no tiene. Solo el Dueño puede consultarlo, así
/// que el error se traga: para el Técnico simplemente no hay casilla de CAI.
final branchFiscalRangeProvider =
    FutureProvider.autoDispose.family<FiscalRange?, String>((ref, branchId) async {
  try {
    final ranges = await ref.watch(tenantRepositoryProvider).fiscalRanges();
    return ranges.where((r) => r.isActive && r.branchId == branchId).firstOrNull;
  } catch (_) {
    return null;
  }
});
