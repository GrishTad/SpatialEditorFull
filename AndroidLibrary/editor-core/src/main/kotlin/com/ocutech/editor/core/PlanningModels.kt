package com.ocutech.editor.core

import com.ocutech.editor.model.AssetType
import com.ocutech.editor.model.ClipEffectsSpec
import com.ocutech.editor.model.EditorProjectDocument
import com.ocutech.editor.model.TransitionType
import com.ocutech.editor.model.ValidationIssue

data class PlannedProject(
    val source: EditorProjectDocument,
    val visualSegments: List<PlannedVisualSegment>,
    val audioLanes: List<PlannedAudioLane>,
    val normalizedDurationMs: Long,
    val warnings: PlanningWarnings = PlanningWarnings(),
)

data class PlannedVisualSegment(
    val clipIndex: Int,
    val assetId: String,
    val assetType: AssetType,
    val kind: SegmentKind,
    val sourceStartMs: Long,
    val sourceEndMs: Long?,
    val durationMs: Long,
    val frameRate: Int?,
    val removeAudio: Boolean,
    val volume: Double,
    val effects: ClipEffectsSpec,
    val transitionType: TransitionType? = null,
)

data class PlannedAudioLane(
    val trackIndex: Int,
    val assetId: String,
    val trimStartMs: Long? = null,
    val trimEndMs: Long? = null,
    val loop: Boolean = false,
    val volume: Double = 1.0,
)

enum class SegmentKind {
    CONTENT,
    FADE_IN,
    FADE_OUT,
}

data class PlanningWarnings(
    val warnings: List<ValidationIssue> = emptyList(),
    val downgradedFeatures: List<String> = emptyList(),
)
