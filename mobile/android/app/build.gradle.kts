import java.util.Properties

plugins {
    id("com.android.application")
    id("kotlin-android")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

// Llave de firma del taller. Vive en `android/key.properties`, que está en .gitignore junto
// con el keystore: es una credencial y no entra al repositorio.
//
// Si no existe, el release se firma con la llave de depuración y todo sigue compilando —CI y
// cualquier máquina nueva incluida—. Lo que no se puede es instalar encima de un APK firmado
// con otra llave: Android lo rechaza y hay que desinstalar, y con eso se van la sesión y la
// cola de fotos pendientes del teléfono.
val propiedadesDeFirma = Properties().apply {
    val archivo = rootProject.file("key.properties")
    if (archivo.exists()) archivo.inputStream().use { load(it) }
}

val hayLlavePropia = propiedadesDeFirma.getProperty("storeFile") != null

// El plugin de Firebase solo se aplica si el archivo del proyecto está en su sitio. Aplicarlo
// sin `google-services.json` rompe la compilación para todo el mundo, y la app funciona sin
// push: los avisos se guardan igual y se ven en la campana. Ver docs/push.md.
if (file("google-services.json").exists()) {
    apply(plugin = "com.google.gms.google-services")
}

android {
    namespace = "com.garaj.garaj_app"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }

    kotlinOptions {
        jvmTarget = JavaVersion.VERSION_11.toString()
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.garaj.garaj_app"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    signingConfigs {
        if (hayLlavePropia) {
            create("release") {
                storeFile = rootProject.file(propiedadesDeFirma.getProperty("storeFile"))
                storePassword = propiedadesDeFirma.getProperty("storePassword")
                keyAlias = propiedadesDeFirma.getProperty("keyAlias")
                keyPassword = propiedadesDeFirma.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.getByName(if (hayLlavePropia) "release" else "debug")
        }
    }
}

flutter {
    source = "../.."
}
