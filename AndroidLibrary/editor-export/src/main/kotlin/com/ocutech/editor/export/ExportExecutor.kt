package com.ocutech.editor.export

import android.content.Context
import android.graphics.Color
import android.net.Uri
import android.os.Looper
import androidx.media3.common.Effect
import androidx.media3.common.MediaItem
import androidx.media3.common.audio.DefaultGainProvider
import androidx.media3.common.audio.GainProcessor
import androidx.media3.effect.BitmapOverlay
import androidx.media3.effect.OverlayEffect
import androidx.media3.effect.RgbMatrix
import androidx.media3.effect.SingleColorLut
import androidx.media3.effect.StaticOverlaySettings
import androidx.media3.transformer.Composition
import androidx.media3.transformer.EditedMediaItem
import androidx.media3.transformer.EditedMediaItemSequence
import androidx.media3.transformer.Effects
import androidx.media3.transformer.ExportException
import androidx.media3.transformer.ExportResult
import androidx.media3.transformer.ProgressHolder
import androidx.media3.transformer.TransformationRequest
import androidx.media3.transformer.Transformer
import com.ocutech.editor.core.PlannedAudioLane
import com.ocutech.editor.core.PlannedProject
import com.ocutech.editor.core.PlannedVisualSegment
import com.ocutech.editor.core.SegmentKind
import com.ocutech.editor.effects.CubeLutParser
import com.ocutech.editor.effects.EffectSpecBuilder
import com.ocutech.editor.model.AssetType
import com.ocutech.editor.model.TransitionType
import com.ocutech.editor.model.ValidationIssue
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import java.io.File
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

interface ExportExecutor {
    suspend fun export(
        jobId: String,
        plan: PlannedProject,
        onProgress: (Int) -> Unit,
    ): ExportOutput

    fun cancel(jobId: String)
}

data class ExportOutput(
    val outputUri: String,
    val warnings: List<ValidationIssue> = emptyList(),
)

class Media3ExportExecutor(
    private val context: Context,
    private val runtimeConfig: ExportRuntimeConfig,
) : ExportExecutor {
    private val activeTransformers = ConcurrentHashMap<String, Transformer>()

    override suspend fun export(
        jobId: String,
        plan: PlannedProject,
        onProgress: (Int) -> Unit,
    ): ExportOutput = withContext(Dispatchers.Main.immediate) {
        val warnings = mutableListOf<ValidationIssue>()
        val destinationUri = plan.source.export.outputUri
        val tempFile = File(context.cacheDir, "unity_export_${jobId}.mp4")
        if (tempFile.exists()) {
            tempFile.delete()
        }

        val composition = buildComposition(plan, warnings)
        val completed = AtomicBoolean(false)
        val completionError = AtomicReference<Throwable?>(null)
        val progressHolder = ProgressHolder()

        val transformer = Transformer.Builder(context)
            .setLooper(Looper.getMainLooper())
            .setAudioMimeType(plan.source.export.audioMimeType)
            .setVideoMimeType(plan.source.export.videoMimeType)
            .setEnsureFileStartsOnVideoFrameEnabled(true)
            .setMaxDelayBetweenMuxerSamplesMs(1_500)
            .build()

        activeTransformers[jobId] = transformer

        try {
            val listener = object : Transformer.Listener {
                override fun onCompleted(composition: Composition, exportResult: ExportResult) {
                    completed.set(true)
                }

                override fun onError(
                    composition: Composition,
                    exportResult: ExportResult,
                    exportException: ExportException,
                ) {
                    completionError.set(exportException)
                    completed.set(true)
                }

                override fun onFallbackApplied(
                    composition: Composition,
                    originalTransformationRequest: TransformationRequest,
                    fallbackTransformationRequest: TransformationRequest,
                ) {
                    warnings += ValidationIssue(
                        code = "TRANSFORMER_FALLBACK_APPLIED",
                        message = "Transformer applied fallback from requested output settings.",
                        field = "export",
                    )
                }
            }

            transformer.addListener(listener)
            try {
                transformer.start(composition, tempFile.absolutePath)
                while (isActive && !completed.get()) {
                    val progressState = transformer.getProgress(progressHolder)
                    if (progressState == Transformer.PROGRESS_STATE_AVAILABLE) {
                        onProgress(progressHolder.progress.coerceIn(0, 100))
                    }
                    delay(200)
                }
            } finally {
                transformer.removeListener(listener)
            }

            if (!isActive) {
                throw CancellationException("Export coroutine is no longer active.")
            }

            completionError.get()?.let { throw it }
            if (!completed.get()) {
                throw IllegalStateException("Transformer finished without terminal callback.")
            }

            if (!tempFile.exists() || tempFile.length() <= 0L) {
                throw IllegalStateException("Transformer completed without creating output file.")
            }

            val persistedUri = persistOutput(tempFile, destinationUri)
            onProgress(100)
            ExportOutput(outputUri = persistedUri, warnings = warnings)
        } catch (cancel: CancellationException) {
            runCatching { transformer.cancel() }
            throw cancel
        } finally {
            activeTransformers.remove(jobId)
            runCatching { tempFile.delete() }
        }
    }

    override fun cancel(jobId: String) {
        activeTransformers[jobId]?.cancel()
    }

    private fun buildComposition(
        plan: PlannedProject,
        warnings: MutableList<ValidationIssue>,
    ): Composition {
        val assetsById = plan.source.assets.associateBy { it.id }
        val overlaysById = plan.source.overlays.associateBy { it.id }

        val visualItems = plan.visualSegments.mapNotNull { segment ->
            val asset = assetsById[segment.assetId] ?: return@mapNotNull null
            if (asset.type !in setOf(AssetType.VIDEO, AssetType.IMAGE)) {
                return@mapNotNull null
            }

            buildVisualEditedItem(
                assetUri = asset.uri,
                segment = segment,
                overlaysById = overlaysById,
                warnings = warnings,
                exportFps = plan.source.export.fps,
            )
        }

        if (visualItems.isEmpty()) {
            throw IllegalStateException("No valid visual segments available for export.")
        }

        val sequences = mutableListOf<EditedMediaItemSequence>()
        sequences += EditedMediaItemSequence.Builder(visualItems)
            .experimentalSetForceVideoTrack(true)
            .build()

        plan.audioLanes.forEach { lane ->
            val asset = assetsById[lane.assetId] ?: return@forEach
            if (asset.type != AssetType.AUDIO) return@forEach
            val audioItem = buildAudioEditedItem(asset.uri, lane, plan.normalizedDurationMs)
            val sequenceBuilder = EditedMediaItemSequence.Builder(listOf(audioItem))
                .experimentalSetForceAudioTrack(true)
            if (lane.loop) {
                sequenceBuilder.setIsLooping(true)
            }
            sequences += sequenceBuilder.build()
        }

        return Composition.Builder(sequences)
            .experimentalSetForceAudioTrack(plan.audioLanes.isNotEmpty())
            .build()
    }

    private fun buildVisualEditedItem(
        assetUri: String,
        segment: PlannedVisualSegment,
        overlaysById: Map<String, com.ocutech.editor.model.OverlaySpec>,
        warnings: MutableList<ValidationIssue>,
        exportFps: Int,
    ): EditedMediaItem {
        val mediaItem = buildVisualMediaItem(assetUri, segment)
        val effects = buildSegmentEffects(segment, overlaysById, warnings)
        return EditedMediaItem.Builder(mediaItem)
            .setDurationUs(segment.durationMs * 1_000)
            .setFrameRate(max(1, segment.frameRate ?: exportFps))
            .setRemoveAudio(segment.removeAudio)
            .setEffects(effects)
            .build()
    }

    private fun buildVisualMediaItem(assetUri: String, segment: PlannedVisualSegment): MediaItem {
        val uri = parseUri(assetUri)
        val builder = MediaItem.Builder().setUri(uri)

        if (segment.assetType == AssetType.IMAGE) {
            builder.setImageDurationMs(segment.durationMs)
        } else {
            builder.setClipStartPositionMs(max(0L, segment.sourceStartMs))
            val sourceEndMs = segment.sourceEndMs
            if (sourceEndMs != null) {
                builder.setClipEndPositionMs(max(segment.sourceStartMs + 1, sourceEndMs))
            }
        }

        return builder.build()
    }

    private fun buildAudioEditedItem(
        assetUri: String,
        lane: PlannedAudioLane,
        timelineDurationMs: Long,
    ): EditedMediaItem {
        val uri = parseUri(assetUri)
        val builder = MediaItem.Builder().setUri(uri)

        val trimStartMs = max(0L, lane.trimStartMs ?: 0L)
        val trimEndMs = lane.trimEndMs?.let { max(trimStartMs + 1, it) }

        if (trimStartMs > 0L) {
            builder.setClipStartPositionMs(trimStartMs)
        }
        if (trimEndMs != null) {
            builder.setClipEndPositionMs(trimEndMs)
        }

        val laneDurationMs = when {
            trimEndMs != null -> max(1L, trimEndMs - trimStartMs)
            else -> max(1L, timelineDurationMs)
        }

        val gainProcessor = GainProcessor(DefaultGainProvider.Builder(lane.volume.toFloat().coerceAtLeast(0f)).build())
        return EditedMediaItem.Builder(builder.build())
            .setRemoveVideo(true)
            .setDurationUs(laneDurationMs * 1_000)
            .setEffects(Effects(listOf(gainProcessor), emptyList()))
            .build()
    }

    private fun buildSegmentEffects(
        segment: PlannedVisualSegment,
        overlaysById: Map<String, com.ocutech.editor.model.OverlaySpec>,
        warnings: MutableList<ValidationIssue>,
    ): Effects {
        val resolved = EffectSpecBuilder.resolve(segment.effects, overlaysById)

        val audioProcessors = mutableListOf<androidx.media3.common.audio.AudioProcessor>()
        if (!segment.removeAudio && segment.volume >= 0.0 && segment.volume != 1.0) {
            audioProcessors += GainProcessor(
                DefaultGainProvider.Builder(segment.volume.toFloat().coerceAtLeast(0f)).build(),
            )
        }

        val videoEffects = mutableListOf<Effect>()

        val colorMatrix = ColorAdjustRgbMatrix(
            brightness = resolved.brightness,
            contrast = resolved.contrast,
            saturation = resolved.saturation,
        )
        if (!colorMatrix.isIdentity()) {
            videoEffects += colorMatrix
        }

        when (segment.kind) {
            SegmentKind.FADE_IN -> {
                if (segment.transitionType == TransitionType.FADE_FROM_BLACK) {
                    videoEffects += FadeRgbMatrix(segment.durationMs * 1_000, fadeIn = true)
                }
            }

            SegmentKind.FADE_OUT -> {
                if (segment.transitionType == TransitionType.DIP_TO_BLACK) {
                    videoEffects += FadeRgbMatrix(segment.durationMs * 1_000, fadeIn = false)
                }
            }

            SegmentKind.CONTENT -> Unit
        }

        val lutAssetPath = resolved.lutAssetPath
        if (!lutAssetPath.isNullOrBlank()) {
            val lut = runCatching { loadLut(lutAssetPath) }.getOrElse { throwable ->
                warnings += ValidationIssue(
                    code = "LUT_LOAD_FAILED",
                    message = throwable.message ?: "Failed to parse/load LUT.",
                    field = "videoTrack[${segment.clipIndex}].effects.lut",
                )
                null
            }
            if (lut != null) {
                videoEffects += lut
            }
        }

        if (resolved.overlays.isNotEmpty()) {
            val textureOverlays = resolved.overlays.mapNotNull { overlay ->
                runCatching {
                    val settings = StaticOverlaySettings.Builder()
                        .setAlphaScale(overlay.opacity.toFloat().coerceIn(0f, 1f))
                        .setScale(overlay.scale.toFloat(), overlay.scale.toFloat())
                        .setBackgroundFrameAnchor(
                            ((overlay.x.toFloat().coerceIn(0f, 1f) * 2f) - 1f),
                            (1f - (overlay.y.toFloat().coerceIn(0f, 1f) * 2f)),
                        )
                        .setOverlayFrameAnchor(0f, 0f)
                        .build()
                    BitmapOverlay.createStaticBitmapOverlay(context, parseUri(overlay.uri), settings)
                }.getOrElse { throwable ->
                    warnings += ValidationIssue(
                        code = "OVERLAY_LOAD_FAILED",
                        message = throwable.message ?: "Failed to load overlay bitmap.",
                        field = "videoTrack[${segment.clipIndex}].effects.overlayIds",
                    )
                    null
                }
            }
            if (textureOverlays.isNotEmpty()) {
                videoEffects += OverlayEffect(textureOverlays)
            }
        }

        return Effects(audioProcessors, videoEffects)
    }

    private fun loadLut(pathOrUri: String): SingleColorLut {
        val text = openText(pathOrUri)
        val cube = CubeLutParser.parse(text)
        val size = cube.size
        val lut = Array(size) { Array(size) { IntArray(size) } }

        var index = 0
        for (b in 0 until size) {
            for (g in 0 until size) {
                for (r in 0 until size) {
                    val row = cube.rows[index++]
                    val normalizedR = normalize(row[0], cube.domainMin[0], cube.domainMax[0])
                    val normalizedG = normalize(row[1], cube.domainMin[1], cube.domainMax[1])
                    val normalizedB = normalize(row[2], cube.domainMin[2], cube.domainMax[2])
                    lut[r][g][b] = Color.rgb(
                        (normalizedR * 255f).roundToInt().coerceIn(0, 255),
                        (normalizedG * 255f).roundToInt().coerceIn(0, 255),
                        (normalizedB * 255f).roundToInt().coerceIn(0, 255),
                    )
                }
            }
        }

        return SingleColorLut.createFromCube(lut)
    }

    private fun normalize(value: Float, minValue: Float, maxValue: Float): Float {
        val range = maxValue - minValue
        if (range <= 0f) return value.coerceIn(0f, 1f)
        return ((value - minValue) / range).coerceIn(0f, 1f)
    }

    private fun openText(pathOrUri: String): String {
        val uri = parseUri(pathOrUri)
        if (uri.scheme.isNullOrBlank() || uri.scheme.equals("file", true)) {
            val filePath = if (uri.scheme.equals("file", true)) uri.path else pathOrUri
            val file = File(filePath.orEmpty())
            if (!file.exists()) {
                throw IllegalArgumentException("LUT file not found: $pathOrUri")
            }
            return file.readText()
        }
        return context.contentResolver.openInputStream(uri)?.bufferedReader()?.use { it.readText() }
            ?: throw IllegalArgumentException("Unable to open LUT uri: $pathOrUri")
    }

    private fun persistOutput(tempFile: File, targetUriString: String): String {
        val targetUri = parseUri(targetUriString)
        when (targetUri.scheme?.lowercase()) {
            "content" -> {
                context.contentResolver.openOutputStream(targetUri, "w")?.use { out ->
                    tempFile.inputStream().use { input -> input.copyTo(out) }
                } ?: throw IllegalStateException("Unable to open output content uri for writing.")
                return targetUri.toString()
            }

            "file" -> {
                val path = targetUri.path ?: throw IllegalStateException("Invalid file output uri.")
                val destination = File(path)
                destination.parentFile?.mkdirs()
                tempFile.copyTo(destination, overwrite = true)
                return targetUri.toString()
            }

            null, "" -> {
                val destination = File(targetUriString)
                destination.parentFile?.mkdirs()
                tempFile.copyTo(destination, overwrite = true)
                return destination.absolutePath
            }

            else -> throw IllegalStateException("Unsupported output uri scheme: ${targetUri.scheme}")
        }
    }

    private fun parseUri(pathOrUri: String): Uri {
        val parsed = Uri.parse(pathOrUri)
        return if (parsed.scheme.isNullOrBlank()) {
            Uri.fromFile(File(pathOrUri))
        } else {
            parsed
        }
    }
}

private class ColorAdjustRgbMatrix(
    brightness: Float,
    contrast: Float,
    saturation: Float,
) : RgbMatrix {
    private val matrix: FloatArray

    init {
        val bScale = (1f + brightness).coerceAtLeast(0f)
        val cScale = contrast.coerceAtLeast(0f)
        val scale = bScale * cScale

        val s = saturation.coerceAtLeast(0f)
        val invSat = 1f - s
        val rw = 0.2126f
        val gw = 0.7152f
        val bw = 0.0722f

        matrix = floatArrayOf(
            (invSat * rw + s) * scale, (invSat * gw) * scale, (invSat * bw) * scale, 0f,
            (invSat * rw) * scale, (invSat * gw + s) * scale, (invSat * bw) * scale, 0f,
            (invSat * rw) * scale, (invSat * gw) * scale, (invSat * bw + s) * scale, 0f,
            0f, 0f, 0f, 1f,
        )
    }

    override fun getMatrix(presentationTimeUs: Long, useHdr: Boolean): FloatArray = matrix

    fun isIdentity(): Boolean {
        val identity = floatArrayOf(
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        )
        return matrix.indices.all { index -> kotlin.math.abs(matrix[index] - identity[index]) < 0.0001f }
    }
}

private class FadeRgbMatrix(
    private val durationUs: Long,
    private val fadeIn: Boolean,
) : RgbMatrix {
    override fun getMatrix(presentationTimeUs: Long, useHdr: Boolean): FloatArray {
        val safeDurationUs = max(1L, durationUs)
        val progress = (presentationTimeUs.toFloat() / safeDurationUs.toFloat()).coerceIn(0f, 1f)
        val factor = if (fadeIn) progress else (1f - progress)
        return floatArrayOf(
            factor, 0f, 0f, 0f,
            0f, factor, 0f, 0f,
            0f, 0f, factor, 0f,
            0f, 0f, 0f, 1f,
        )
    }
}
