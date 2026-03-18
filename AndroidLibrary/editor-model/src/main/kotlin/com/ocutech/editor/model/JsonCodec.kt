package com.ocutech.editor.model

import kotlinx.serialization.encodeToString
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.Json

object JsonCodec {
    val json: Json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
        encodeDefaults = true
    }

    inline fun <reified T> decode(value: String): T = json.decodeFromString(value)

    inline fun <reified T> encode(value: T): String = json.encodeToString(value)
}
