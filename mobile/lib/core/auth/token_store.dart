import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Tokens en el llavero del sistema (Keychain / Keystore), no en SharedPreferences:
/// un refresh token de 30 días en almacenamiento plano es una credencial de larga vida
/// expuesta en un dispositivo que se usa dentro del taller.
class TokenStore {
  TokenStore([FlutterSecureStorage? storage])
      : _storage = storage ?? const FlutterSecureStorage();

  static const _accessTokenKey = 'garaj.accessToken';
  static const _refreshTokenKey = 'garaj.refreshToken';

  final FlutterSecureStorage _storage;

  Future<String?> readAccessToken() => _storage.read(key: _accessTokenKey);

  Future<String?> readRefreshToken() => _storage.read(key: _refreshTokenKey);

  Future<void> save({required String accessToken, required String refreshToken}) async {
    await _storage.write(key: _accessTokenKey, value: accessToken);
    await _storage.write(key: _refreshTokenKey, value: refreshToken);
  }

  Future<void> clear() async {
    await _storage.delete(key: _accessTokenKey);
    await _storage.delete(key: _refreshTokenKey);
  }
}
