allprojects {
    repositories {
        google()
        mavenCentral()
    }
}

val newBuildDir: Directory =
    rootProject.layout.buildDirectory
        .dir("../../build")
        .get()
rootProject.layout.buildDirectory.value(newBuildDir)

subprojects {
    val newSubprojectBuildDir: Directory = newBuildDir.dir(project.name)
    project.layout.buildDirectory.value(newSubprojectBuildDir)
}
subprojects {
    // `receive_sharing_intent` compila su Java contra la 1.8 y deja Kotlin en la versión del
    // JDK con el que se construye. Gradle corta al ver las dos distintas, y el error solo sale
    // al armar el paquete de release, no al analizar ni al correr en depuración. Se baja
    // Kotlin a la que el propio plugin eligió para su Java —lo contrario no se puede: la
    // versión de Java la fija él al evaluarse—. Solo a él: los demás ya vienen parejos.
    if (name == "receive_sharing_intent") {
        tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile>().configureEach {
            compilerOptions.jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_1_8)
        }
    }

    project.evaluationDependsOn(":app")
}

tasks.register<Delete>("clean") {
    delete(rootProject.layout.buildDirectory)
}
