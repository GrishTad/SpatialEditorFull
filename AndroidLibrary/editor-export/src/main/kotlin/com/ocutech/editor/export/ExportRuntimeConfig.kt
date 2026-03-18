package com.ocutech.editor.export

data class ExportRuntimeConfig(
    val libraryVersion: String = "0.1.0",
    val schemaVersion: Int = 1,
    val keepLastRequest: Boolean = true,
    val debugLogging: Boolean = false,
)
