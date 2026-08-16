// Espejo de los DTOs de `Garaj.Application.Auth`. En la Fase 1, cuando aparezcan las
// entidades de negocio, conviene pasar a freezed + json_serializable; para el login la
// deserialización manual evita arrastrar build_runner desde el día uno.

enum AppRole {
  owner('Owner'),
  technician('Technician'),
  customer('Customer');

  const AppRole(this.value);

  final String value;

  static AppRole fromValue(String value) => AppRole.values.firstWhere(
        (role) => role.value == value,
        orElse: () => AppRole.customer,
      );
}

class BranchSummary {
  const BranchSummary({required this.id, required this.name, this.code});

  factory BranchSummary.fromJson(Map<String, dynamic> json) => BranchSummary(
        id: json['id'] as String,
        name: json['name'] as String,
        code: json['code'] as String?,
      );

  final String id;
  final String name;
  final String? code;
}

class CurrentUser {
  const CurrentUser({
    required this.id,
    required this.email,
    required this.fullName,
    required this.role,
    required this.tenantId,
    required this.tenantName,
    required this.branches,
    this.tenantLogoUrl,
    this.customerId,
    this.subscription,
  });

  factory CurrentUser.fromJson(Map<String, dynamic> json) => CurrentUser(
        id: json['id'] as String,
        email: json['email'] as String,
        fullName: json['fullName'] as String,
        role: AppRole.fromValue(json['role'] as String),
        tenantId: json['tenantId'] as String,
        tenantName: json['tenantName'] as String,
        tenantLogoUrl: json['tenantLogoUrl'] as String?,
        branches: (json['branches'] as List<dynamic>)
            .map((e) => BranchSummary.fromJson(e as Map<String, dynamic>))
            .toList(),
        customerId: json['customerId'] as String?,
        subscription: json['subscription'] == null
            ? null
            : SubscriptionInfo.fromJson(json['subscription'] as Map<String, dynamic>),
      );

  final String id;
  final String email;
  final String fullName;
  final AppRole role;
  final String tenantId;
  final String tenantName;

  /// Ruta del logo del taller relativa a la base de la API, o null si no subió ninguno.
  /// La sirve una ruta pública porque `Image.network` no manda cabecera de autorización.
  final String? tenantLogoUrl;

  final List<BranchSummary> branches;
  final String? customerId;

  /// Cómo va el taller con su mensualidad. **Null salvo para el Dueño**: el backend no se lo
  /// manda al Técnico ni al Cliente, así que aquí no hay que acordarse de esconderlo.
  final SubscriptionInfo? subscription;
}

/// El aviso de cobro, ya resuelto por el backend —el texto incluido— para que el panel y la
/// app digan exactamente lo mismo.
class SubscriptionInfo {
  const SubscriptionInfo({
    required this.state,
    required this.canWrite,
    required this.message,
    this.paidThrough,
    this.daysLeft,
    this.readOnlyOn,
    this.agreementThrough,
    this.agreementNote,
  });

  factory SubscriptionInfo.fromJson(Map<String, dynamic> json) => SubscriptionInfo(
        state: json['state'] as String,
        canWrite: json['canWrite'] as bool,
        message: json['message'] as String? ?? '',
        paidThrough: json['paidThrough'] as String?,
        daysLeft: json['daysLeft'] as int?,
        readOnlyOn: json['readOnlyOn'] as String?,
        agreementThrough: json['agreementThrough'] as String?,
        agreementNote: json['agreementNote'] as String?,
      );

  /// `Active`, `DueSoon`, `Grace`, `ReadOnly` o `Suspended`.
  final String state;

  /// Si el taller puede registrar trabajo. En false la API responde 402 a lo que escribe.
  final bool canWrite;

  final String message;
  final String? paidThrough;
  final int? daysLeft;
  final String? readOnlyOn;
  final String? agreementThrough;
  final String? agreementNote;

  /// Estando al día y sin acuerdo no se le dice nada: un aviso permanente deja de leerse.
  bool get shouldWarn => state != 'Active' || agreementThrough != null;
}

class AuthResponse {
  const AuthResponse({
    required this.accessToken,
    required this.refreshToken,
    required this.expiresAt,
    required this.user,
  });

  factory AuthResponse.fromJson(Map<String, dynamic> json) => AuthResponse(
        accessToken: json['accessToken'] as String,
        refreshToken: json['refreshToken'] as String,
        expiresAt: DateTime.parse(json['expiresAt'] as String),
        user: CurrentUser.fromJson(json['user'] as Map<String, dynamic>),
      );

  final String accessToken;
  final String refreshToken;
  final DateTime expiresAt;
  final CurrentUser user;
}
