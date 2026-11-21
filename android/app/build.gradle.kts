import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// Version lives in version.properties so CI and the in-app updater agree on it.
val versionProps = Properties().apply { file("../version.properties").inputStream().use { load(it) } }
val agVersionName: String = versionProps.getProperty("versionName")
val agVersionCode: Int = versionProps.getProperty("versionCode").toInt()

android {
    namespace = "com.griddim.arcadegridz"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.griddim.arcadegridz"
        minSdk = 24
        targetSdk = 35
        versionCode = agVersionCode
        versionName = agVersionName
    }

    signingConfigs {
        create("release") {
            // CI passes a private keystore through env vars when one is configured; otherwise the committed test key signs.
            val ks = System.getenv("AG_KEYSTORE_FILE")
            if (!ks.isNullOrBlank()) {
                storeFile = file(ks)
                storePassword = System.getenv("AG_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("AG_KEY_ALIAS")
                keyPassword = System.getenv("AG_KEY_PASSWORD")
            } else {
                storeFile = file("../signing/arcadegridz-test.jks")
                storePassword = "agtest-2026"
                keyAlias = "arcadegridz"
                keyPassword = "agtest-2026"
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("release")
        }
        debug {
            signingConfig = signingConfigs.getByName("release")
        }
    }

    sourceSets["main"].assets.srcDirs("src/main/assets", "build/agassets")

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { buildConfig = true }
    lint { abortOnError = false; checkReleaseBuilds = false }
}

dependencies {
    implementation("androidx.webkit:webkit:1.12.1")
}
