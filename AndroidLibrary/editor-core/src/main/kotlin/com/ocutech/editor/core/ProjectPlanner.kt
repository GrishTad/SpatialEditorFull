package com.ocutech.editor.core

import com.ocutech.editor.model.AssetRef
import com.ocutech.editor.model.AssetType
import com.ocutech.editor.model.EditorProjectDocument
import com.ocutech.editor.model.ValidationIssue
import com.ocutech.editor.model.ValidationReport
import kotlin.math.max
import kotlin.math.min

data class PlanningResult(
    val report: ValidationReport,
    val plannedProject: PlannedProject?,
)

class ProjectPlanner {
    fun validate(project: EditorProjectDocument): ValidationReport {
        val errors = mutableListOf<ValidationIssue>()
        val warnings = mutableListOf<ValidationIssue>()
        val downgraded = mutableSetOf<String>()

        if (project.version != 1) {
            errors += ValidationIssue(
                code = "UNSUPPORTED_SCHEMA_VERSION",
                message = "Only schema version 1 is supported.",
                field = "version",
            )
        }

        if (project.videoTrack.isEmpty()) {
            errors += ValidationIssue(
                code = "EMPTY_VIDEO_TRACK",
                message = "videoTrack must contain at least one clip.",
                field = "videoTrack",
            )
        }

        if (project.export.outputUri.isBlank()) {
            errors += ValidationIssue(
                code = "INVALID_OUTPUT_URI",
                message = "export.outputUri must be a non-empty URI.",
                field = "export.outputUri",
            )
        }

        if (project.export.width <= 0 || project.export.height <= 0 || project.export.fps <= 0) {
            errors += ValidationIssue(
                code = "INVALID_EXPORT_DIMENSIONS",
                message = "export width/height/fps must be positive values.",
                field = "export",
            )
        }

        val assetsById = project.assets.associateBy { it.id }
        if (assetsById.size != project.assets.size) {
            errors += ValidationIssue(
                code = "DUPLICATE_ASSET_ID",
                message = "assets contains duplicate ids.",
                field = "assets",
            )
        }

        project.assets.forEachIndexed { index, asset ->
            if (asset.id.isBlank()) {
                errors += ValidationIssue(
                    code = "EMPTY_ASSET_ID",
                    message = "Asset id cannot be blank.",
                    field = "assets[$index].id",
                )
            }
            if (asset.uri.isBlank()) {
                errors += ValidationIssue(
                    code = "EMPTY_ASSET_URI",
                    message = "Asset uri cannot be blank.",
                    field = "assets[$index].uri",
                )
            }
        }

        val overlaysById = project.overlays.associateBy { it.id }
        if (overlaysById.size != project.overlays.size) {
            errors += ValidationIssue(
                code = "DUPLICATE_OVERLAY_ID",
                message = "overlays contains duplicate ids.",
                field = "overlays",
            )
        }

        project.overlays.forEachIndexed { index, overlay ->
            if (overlay.id.isBlank()) {
                errors += ValidationIssue(
                    code = "EMPTY_OVERLAY_ID",
                    message = "Overlay id cannot be blank.",
                    field = "overlays[$index].id",
                )
            }
            if (overlay.uri.isBlank()) {
                errors += ValidationIssue(
                    code = "EMPTY_OVERLAY_URI",
                    message = "Overlay uri cannot be blank.",
                    field = "overlays[$index].uri",
                )
            }
            if (overlay.scale <= 0.0) {
                errors += ValidationIssue(
                    code = "INVALID_OVERLAY_SCALE",
                    message = "Overlay scale must be > 0.",
                    field = "overlays[$index].scale",
                )
            }
            if (overlay.opacity < 0.0 || overlay.opacity > 1.0) {
                errors += ValidationIssue(
                    code = "INVALID_OVERLAY_OPACITY",
                    message = "Overlay opacity must be within [0,1].",
                    field = "overlays[$index].opacity",
                )
            }
            if (overlay.x < 0.0 || overlay.x > 1.0 || overlay.y < 0.0 || overlay.y > 1.0) {
                warnings += ValidationIssue(
                    code = "OVERLAY_POSITION_OUT_OF_RANGE",
                    message = "Overlay x/y are expected in [0,1]. Values will be clamped.",
                    field = "overlays[$index]",
                )
                downgraded += "overlay_position_clamp"
            }
        }

        project.videoTrack.forEachIndexed { index, clip ->
            val asset = assetsById[clip.assetId]
            val trimStartMs = clip.trimStartMs
            val trimEndMs = clip.trimEndMs
            val explicitDurationMs = clip.durationMs
            val frameRate = clip.frameRate

            if (asset == null) {
                errors += ValidationIssue(
                    code = "MISSING_VIDEO_ASSET",
                    message = "videoTrack clip references missing assetId '${clip.assetId}'.",
                    field = "videoTrack[$index].assetId",
                )
            } else if (asset.type !in setOf(AssetType.VIDEO, AssetType.IMAGE)) {
                errors += ValidationIssue(
                    code = "INVALID_VIDEO_ASSET_TYPE",
                    message = "videoTrack requires video or image assets.",
                    field = "videoTrack[$index].assetId",
                )
            }

            if (clip.volume < 0.0) {
                errors += ValidationIssue(
                    code = "INVALID_CLIP_VOLUME",
                    message = "Clip volume must be >= 0.",
                    field = "videoTrack[$index].volume",
                )
            }

            if (frameRate != null && frameRate <= 0) {
                errors += ValidationIssue(
                    code = "INVALID_CLIP_FRAME_RATE",
                    message = "frameRate must be > 0 when specified.",
                    field = "videoTrack[$index].frameRate",
                )
            }

            if (trimStartMs != null && trimStartMs < 0) {
                errors += ValidationIssue(
                    code = "INVALID_TRIM_START",
                    message = "trimStartMs must be >= 0.",
                    field = "videoTrack[$index].trimStartMs",
                )
            }

            if (trimEndMs != null && trimEndMs < 0) {
                errors += ValidationIssue(
                    code = "INVALID_TRIM_END",
                    message = "trimEndMs must be >= 0.",
                    field = "videoTrack[$index].trimEndMs",
                )
            }

            if (trimStartMs != null && trimEndMs != null && trimEndMs <= trimStartMs) {
                errors += ValidationIssue(
                    code = "INVALID_TRIM_RANGE",
                    message = "trimEndMs must be greater than trimStartMs.",
                    field = "videoTrack[$index]",
                )
            }

            if (asset?.type == AssetType.IMAGE && (explicitDurationMs == null || explicitDurationMs <= 0)) {
                errors += ValidationIssue(
                    code = "IMAGE_DURATION_REQUIRED",
                    message = "Image clips must define durationMs > 0.",
                    field = "videoTrack[$index].durationMs",
                )
            }

            clip.effects.overlayIds.forEach { overlayId ->
                if (!overlaysById.containsKey(overlayId)) {
                    errors += ValidationIssue(
                        code = "MISSING_OVERLAY_REFERENCE",
                        message = "Referenced overlay '$overlayId' does not exist.",
                        field = "videoTrack[$index].effects.overlayIds",
                    )
                }
            }

            val estimatedDuration = estimateClipDuration(trimStartMs, trimEndMs, explicitDurationMs)
            val transitionInMs = clip.transitionIn?.durationMs ?: 0
            val transitionOutMs = clip.transitionOut?.durationMs ?: 0
            if (transitionInMs < 0 || transitionOutMs < 0) {
                errors += ValidationIssue(
                    code = "INVALID_TRANSITION_DURATION",
                    message = "transition duration must be >= 0.",
                    field = "videoTrack[$index]",
                )
            }
            if (estimatedDuration > 0 && transitionInMs + transitionOutMs > estimatedDuration) {
                warnings += ValidationIssue(
                    code = "TRANSITION_CLAMPED",
                    message = "Transition durations exceed clip duration and will be clamped.",
                    field = "videoTrack[$index]",
                )
                downgraded += "transition_clamp"
            }
        }

        project.audioTracks.forEachIndexed { index, clip ->
            val asset = assetsById[clip.assetId]
            val trimStartMs = clip.trimStartMs
            val trimEndMs = clip.trimEndMs
            if (asset == null) {
                errors += ValidationIssue(
                    code = "MISSING_AUDIO_ASSET",
                    message = "audioTracks clip references missing assetId '${clip.assetId}'.",
                    field = "audioTracks[$index].assetId",
                )
            } else if (asset.type != AssetType.AUDIO) {
                errors += ValidationIssue(
                    code = "INVALID_AUDIO_ASSET_TYPE",
                    message = "audioTracks requires audio assets.",
                    field = "audioTracks[$index].assetId",
                )
            }

            if (clip.volume < 0.0) {
                errors += ValidationIssue(
                    code = "INVALID_AUDIO_VOLUME",
                    message = "Audio track volume must be >= 0.",
                    field = "audioTracks[$index].volume",
                )
            }

            if (trimStartMs != null && trimStartMs < 0) {
                errors += ValidationIssue(
                    code = "INVALID_AUDIO_TRIM_START",
                    message = "audioTracks.trimStartMs must be >= 0.",
                    field = "audioTracks[$index].trimStartMs",
                )
            }

            if (trimEndMs != null && trimEndMs < 0) {
                errors += ValidationIssue(
                    code = "INVALID_AUDIO_TRIM_END",
                    message = "audioTracks.trimEndMs must be >= 0.",
                    field = "audioTracks[$index].trimEndMs",
                )
            }

            if (trimStartMs != null && trimEndMs != null && trimEndMs <= trimStartMs) {
                errors += ValidationIssue(
                    code = "INVALID_AUDIO_TRIM_RANGE",
                    message = "audioTracks.trimEndMs must be greater than trimStartMs.",
                    field = "audioTracks[$index]",
                )
            }

            if (clip.loop && trimEndMs == null) {
                warnings += ValidationIssue(
                    code = "AUDIO_LOOP_WITHOUT_TRIM_END",
                    message = "Looping audio without trimEndMs may produce long exports. trimEndMs is recommended.",
                    field = "audioTracks[$index]",
                )
                downgraded += "audio_loop_no_trim_end"
            }
        }

        val normalizedDuration = project.videoTrack.sumOf {
            estimateClipDuration(it.trimStartMs, it.trimEndMs, it.durationMs)
        }

        if (normalizedDuration <= 0) {
            warnings += ValidationIssue(
                code = "ZERO_DURATION_TIMELINE",
                message = "Timeline duration resolved to 0ms; provide trim bounds or explicit image durations.",
                field = "videoTrack",
            )
        }

        return ValidationReport(
            errors = errors,
            warnings = warnings,
            downgradedFeatures = downgraded.toList(),
            normalizedDurationMs = max(0L, normalizedDuration),
        )
    }

    fun plan(project: EditorProjectDocument): PlanningResult {
        val report = validate(project)
        if (report.errors.isNotEmpty()) {
            return PlanningResult(report = report, plannedProject = null)
        }

        val assetsById = project.assets.associateBy(AssetRef::id)
        val segments = mutableListOf<PlannedVisualSegment>()

        project.videoTrack.forEachIndexed { index, clip ->
            val asset = assetsById[clip.assetId] ?: return@forEachIndexed
            val sourceStart = max(0L, clip.trimStartMs ?: 0L)
            val clipDuration = estimateClipDuration(
                trimStartMs = clip.trimStartMs,
                trimEndMs = clip.trimEndMs,
                explicitDurationMs = clip.durationMs,
            )
            if (clipDuration <= 0) {
                return@forEachIndexed
            }

            val requestedIn = max(0L, clip.transitionIn?.durationMs ?: 0L)
            val requestedOut = max(0L, clip.transitionOut?.durationMs ?: 0L)
            val fadeInMs = min(requestedIn, clipDuration)
            val fadeOutMs = min(requestedOut, max(0L, clipDuration - fadeInMs))
            val contentMs = max(0L, clipDuration - fadeInMs - fadeOutMs)

            var cursorMs = sourceStart

            if (fadeInMs > 0) {
                segments += PlannedVisualSegment(
                    clipIndex = index,
                    assetId = clip.assetId,
                    assetType = asset.type,
                    kind = SegmentKind.FADE_IN,
                    sourceStartMs = cursorMs,
                    sourceEndMs = cursorMs + fadeInMs,
                    durationMs = fadeInMs,
                    frameRate = clip.frameRate,
                    removeAudio = clip.removeAudio,
                    volume = clip.volume,
                    effects = clip.effects,
                    transitionType = clip.transitionIn?.type,
                )
                cursorMs += fadeInMs
            }

            if (contentMs > 0) {
                segments += PlannedVisualSegment(
                    clipIndex = index,
                    assetId = clip.assetId,
                    assetType = asset.type,
                    kind = SegmentKind.CONTENT,
                    sourceStartMs = cursorMs,
                    sourceEndMs = cursorMs + contentMs,
                    durationMs = contentMs,
                    frameRate = clip.frameRate,
                    removeAudio = clip.removeAudio,
                    volume = clip.volume,
                    effects = clip.effects,
                )
                cursorMs += contentMs
            }

            if (fadeOutMs > 0) {
                segments += PlannedVisualSegment(
                    clipIndex = index,
                    assetId = clip.assetId,
                    assetType = asset.type,
                    kind = SegmentKind.FADE_OUT,
                    sourceStartMs = cursorMs,
                    sourceEndMs = cursorMs + fadeOutMs,
                    durationMs = fadeOutMs,
                    frameRate = clip.frameRate,
                    removeAudio = clip.removeAudio,
                    volume = clip.volume,
                    effects = clip.effects,
                    transitionType = clip.transitionOut?.type,
                )
            }
        }

        val plannedAudio = project.audioTracks.mapIndexed { index, track ->
            PlannedAudioLane(
                trackIndex = index,
                assetId = track.assetId,
                trimStartMs = track.trimStartMs,
                trimEndMs = track.trimEndMs,
                loop = track.loop,
                volume = track.volume,
            )
        }

        return PlanningResult(
            report = report,
            plannedProject = PlannedProject(
                source = project,
                visualSegments = segments,
                audioLanes = plannedAudio,
                normalizedDurationMs = report.normalizedDurationMs,
                warnings = PlanningWarnings(
                    warnings = report.warnings,
                    downgradedFeatures = report.downgradedFeatures,
                ),
            ),
        )
    }

    private fun estimateClipDuration(
        trimStartMs: Long?,
        trimEndMs: Long?,
        explicitDurationMs: Long?,
    ): Long {
        val trimmed = if (trimStartMs != null && trimEndMs != null) trimEndMs - trimStartMs else null
        return when {
            trimmed != null -> max(0L, trimmed)
            explicitDurationMs != null -> max(0L, explicitDurationMs)
            else -> 0L
        }
    }
}
