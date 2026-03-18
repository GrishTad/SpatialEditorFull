import groovy.lang.Closure
import java.io.File

plugins {
    alias(libs.plugins.android.fused.library)
    `maven-publish`
}

androidFusedLibrary {
    namespace = "com.ocutech.editor.fused"
    val minSdkMethod = this::class.java.methods.firstOrNull {
        it.name == "minSdk" && it.parameterTypes.size == 1 && Closure::class.java.isAssignableFrom(it.parameterTypes[0])
    } ?: error("Fused plugin minSdk(Closure) API was not found.")

    val closure = object : Closure<Any?>(this, this) {
        @Suppress("unused")
        fun doCall(spec: Any) {
            val release = spec.javaClass.methods.first {
                it.name == "release" &&
                    it.parameterTypes.size == 1 &&
                    (it.parameterTypes[0] == Int::class.javaPrimitiveType || it.parameterTypes[0] == Integer::class.java)
            }
            val setVersion = spec.javaClass.methods.first {
                it.name == "setVersion" && it.parameterTypes.size == 1
            }
            val minSdkVersion = release.invoke(spec, 24)
            setVersion.invoke(spec, minSdkVersion)
        }
    }
    minSdkMethod.invoke(this, closure)

    val consumerRulesMethod = this::class.java.methods.firstOrNull {
        it.name == "consumerProguardFiles" && it.parameterTypes.size == 1 && it.parameterTypes[0].isArray
    }
    consumerRulesMethod?.invoke(this, arrayOf("consumer-rules.pro"))
}

dependencies {
    include(project(":editor-model"))
    include(project(":editor-core"))
    include(project(":editor-effects"))
    include(project(":editor-export"))
    include(project(":editor-api"))

    // Explicit Media3 includes, because fused packaging does not include transitives.
    include(libs.androidx.media3.common)
    include(libs.androidx.media3.effect)
    include(libs.androidx.media3.transformer)
    include(libs.androidx.media3.database)
    include(libs.androidx.media3.datasource)
    include(libs.androidx.media3.container)
    include(libs.androidx.media3.exoplayer)
    include(libs.androidx.media3.muxer)
    include(libs.androidx.media3.decoder)
    include(libs.androidx.media3.extractor)

    // Keep fused runtime lean to avoid duplicate classes with Unity/other plugins.
    // Coroutines/serialization are typically already present in Unity Android projects.
}

publishing {
    publications {
        register<MavenPublication>("release") {
            groupId = "com.ocutech.media3editor"
            artifactId = "unity-media3-editor"
            version = "0.1.0"
            from(components["fusedLibraryComponent"])
        }
    }
    repositories {
        maven {
            name = "localFusedRepo"
            url = uri(layout.buildDirectory.dir("repo"))
        }
    }
}

tasks.register("bundleWithConsumerRules") {
    dependsOn("bundle")
    doLast {
        val aarFile = layout.buildDirectory.file("outputs/aar/editor-fused.aar").get().asFile
        if (!aarFile.exists()) {
            throw GradleException("Fused AAR not found at ${aarFile.absolutePath}")
        }

        val tempDir = layout.buildDirectory.dir("tmp/consumerRules").get().asFile
        if (!tempDir.exists()) {
            tempDir.mkdirs()
        }
        val proguardTxt = File(tempDir, "proguard.txt")
        proguardTxt.writeText(file("consumer-rules.pro").readText())
        val aarMetadata = File(tempDir, "META-INF/com/android/build/gradle/aar-metadata.properties")
        aarMetadata.parentFile.mkdirs()
        aarMetadata.writeText(
            """
            aarFormatVersion=1.0
            aarMetadataVersion=1.0
            minCompileSdk=34
            minCompileSdkExtension=0
            minAndroidGradlePluginVersion=1.0.0
            coreLibraryDesugaringEnabled=false
            """.trimIndent()
        )

        val isWindows = System.getProperty("os.name").contains("Windows", ignoreCase = true)
        val jarTool = File(System.getProperty("java.home"), "bin/jar" + if (isWindows) ".exe" else "")
        val process = ProcessBuilder(
            jarTool.absolutePath,
            "uf",
            aarFile.absolutePath,
            "-C",
            tempDir.absolutePath,
            "proguard.txt",
        )
            .redirectErrorStream(true)
            .inheritIO()
            .start()
        val exitCode = process.waitFor()
        if (exitCode != 0) {
            throw GradleException("Failed to inject consumer proguard rules into fused AAR.")
        }

        val metadataProcess = ProcessBuilder(
            jarTool.absolutePath,
            "uf",
            aarFile.absolutePath,
            "-C",
            tempDir.absolutePath,
            "META-INF/com/android/build/gradle/aar-metadata.properties",
        )
            .redirectErrorStream(true)
            .inheritIO()
            .start()
        val metadataExitCode = metadataProcess.waitFor()
        if (metadataExitCode != 0) {
            throw GradleException("Failed to update fused AAR metadata.")
        }
    }
}
