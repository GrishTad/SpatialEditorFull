package com.ocutech.editor.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

@Serializable
data class EditorProjectDocument(
    val version: Int = 1,
    val assets: List<AssetRef> = emptyList(),
    val videoTrack: List<VideoClipSpec> = emptyList(),
    val audioTracks: List<AudioClipSpec> = emptyList(),
    val overlays: List<OverlaySpec> = emptyList(),
    val export: ExportSpec,
)

@Serializable
data class AssetRef(
    val id: String,
    val type: AssetType,
    val uri: String,
)

@Serializable
enum class AssetType {
    @SerialName("video")
    VIDEO,

    @SerialName("image")
    IMAGE,

    @SerialName("audio")
    AUDIO,
}

@Serializable
data class VideoClipSpec(
    val assetId: String,
    val trimStartMs: Long? = null,
    val trimEndMs: Long? = null,
    val durationMs: Long? = null,
    val frameRate: Int? = null,
    val removeAudio: Boolean = false,
    val volume: Double = 1.0,
    val effects: ClipEffectsSpec = ClipEffectsSpec(),
    val transitionIn: TransitionSpec? = null,
    val transitionOut: TransitionSpec? = null,
)

@Serializable
data class AudioClipSpec(
    val assetId: String,
    val trimStartMs: Long? = null,
    val trimEndMs: Long? = null,
    val loop: Boolean = false,
    val volume: Double = 1.0,
)

@Serializable
data class ClipEffectsSpec(
    val brightness: Double? = null,
    val contrast: Double? = null,
    val saturation: Double? = null,
    val lut: String? = null,
    val overlayIds: List<String> = emptyList(),
)

@Serializable
data class TransitionSpec(
    val type: TransitionType,
    val durationMs: Long,
)

@Serializable
enum class TransitionType {
    @SerialName("dip_to_black")
    DIP_TO_BLACK,

    @SerialName("fade_from_black")
    FADE_FROM_BLACK,
}

@Serializable
data class OverlaySpec(
    val id: String,
    val uri: String,
    val x: Double,
    val y: Double,
    val scale: Double,
    val opacity: Double = 1.0,
    val zIndex: Int = 0,
)

@Serializable
data class ExportSpec(
    val outputUri: String,
    val videoMimeType: String = "video/avc",
    val audioMimeType: String = "audio/mp4a-latm",
    val width: Int = 1920,
    val height: Int = 1080,
    val fps: Int = 30,
)

@Serializable
data class EditorConfig(
    val schemaVersion: Int = 1,
    val keepLastRequest: Boolean = true,
    val debugLogging: Boolean = false,
)
