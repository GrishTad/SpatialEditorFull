package com.ocutech.editor.core

import com.ocutech.editor.model.AssetRef
import com.ocutech.editor.model.AssetType
import com.ocutech.editor.model.AudioClipSpec
import com.ocutech.editor.model.ClipEffectsSpec
import com.ocutech.editor.model.EditorProjectDocument
import com.ocutech.editor.model.ExportSpec
import com.ocutech.editor.model.OverlaySpec
import com.ocutech.editor.model.TransitionSpec
import com.ocutech.editor.model.TransitionType
import com.ocutech.editor.model.VideoClipSpec
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

class ProjectPlannerTest {
    @Test
    fun splitsTransitionSegmentsWithoutOverlap() {
        val planner = ProjectPlanner()
        val project = EditorProjectDocument(
            assets = listOf(
                AssetRef(id = "v1", type = AssetType.VIDEO, uri = "content://demo/video1"),
            ),
            videoTrack = listOf(
                VideoClipSpec(
                    assetId = "v1",
                    trimStartMs = 0,
                    trimEndMs = 5000,
                    effects = ClipEffectsSpec(),
                    transitionIn = TransitionSpec(
                        type = TransitionType.FADE_FROM_BLACK,
                        durationMs = 500,
                    ),
                    transitionOut = TransitionSpec(
                        type = TransitionType.DIP_TO_BLACK,
                        durationMs = 500,
                    ),
                ),
            ),
            export = ExportSpec(outputUri = "content://demo/output.mp4"),
        )

        val planning = planner.plan(project)
        assertTrue(planning.report.errors.isEmpty(), "Expected no planning errors")
        val segments = planning.plannedProject?.visualSegments.orEmpty()
        assertEquals(3, segments.size)
        assertEquals(SegmentKind.FADE_IN, segments[0].kind)
        assertEquals(500L, segments[0].durationMs)
        assertEquals(SegmentKind.CONTENT, segments[1].kind)
        assertEquals(4000L, segments[1].durationMs)
        assertEquals(SegmentKind.FADE_OUT, segments[2].kind)
        assertEquals(500L, segments[2].durationMs)
    }

    @Test
    fun reportsSchemaAndOverlayReferenceErrors() {
        val planner = ProjectPlanner()
        val project = EditorProjectDocument(
            version = 2,
            assets = listOf(
                AssetRef(id = "v1", type = AssetType.VIDEO, uri = "content://demo/video1"),
            ),
            videoTrack = listOf(
                VideoClipSpec(
                    assetId = "v1",
                    trimStartMs = 0,
                    trimEndMs = 2000,
                    effects = ClipEffectsSpec(overlayIds = listOf("missing_overlay")),
                ),
            ),
            export = ExportSpec(outputUri = "content://demo/output.mp4"),
        )

        val report = planner.validate(project)
        assertTrue(report.errors.any { it.code == "UNSUPPORTED_SCHEMA_VERSION" })
        assertTrue(report.errors.any { it.code == "MISSING_OVERLAY_REFERENCE" })
    }

    @Test
    fun clampsTransitionsOnShortClip() {
        val planner = ProjectPlanner()
        val project = EditorProjectDocument(
            assets = listOf(
                AssetRef(id = "v1", type = AssetType.VIDEO, uri = "content://demo/video1"),
            ),
            videoTrack = listOf(
                VideoClipSpec(
                    assetId = "v1",
                    trimStartMs = 0,
                    trimEndMs = 1000,
                    transitionIn = TransitionSpec(TransitionType.FADE_FROM_BLACK, 800),
                    transitionOut = TransitionSpec(TransitionType.DIP_TO_BLACK, 800),
                ),
            ),
            export = ExportSpec(outputUri = "content://demo/output.mp4"),
        )

        val planning = planner.plan(project)
        assertTrue(planning.report.errors.isEmpty())
        assertTrue(planning.report.warnings.any { it.code == "TRANSITION_CLAMPED" })
        val planned = assertNotNull(planning.plannedProject)
        val totalDuration = planned.visualSegments.sumOf { it.durationMs }
        assertEquals(1000L, totalDuration)
    }

    @Test
    fun warnsForLoopingAudioWithoutTrimEnd() {
        val planner = ProjectPlanner()
        val project = EditorProjectDocument(
            assets = listOf(
                AssetRef(id = "v1", type = AssetType.VIDEO, uri = "content://demo/video1"),
                AssetRef(id = "a1", type = AssetType.AUDIO, uri = "content://demo/music1"),
            ),
            videoTrack = listOf(
                VideoClipSpec(
                    assetId = "v1",
                    trimStartMs = 0,
                    trimEndMs = 3000,
                ),
            ),
            audioTracks = listOf(
                AudioClipSpec(
                    assetId = "a1",
                    trimStartMs = 0,
                    trimEndMs = null,
                    loop = true,
                    volume = 0.6,
                ),
            ),
            export = ExportSpec(outputUri = "content://demo/output.mp4"),
        )

        val report = planner.validate(project)
        assertTrue(report.errors.isEmpty())
        assertTrue(report.warnings.any { it.code == "AUDIO_LOOP_WITHOUT_TRIM_END" })
        assertTrue(report.downgradedFeatures.contains("audio_loop_no_trim_end"))
    }

    @Test
    fun resolvesOverlayAndAudioLanePlan() {
        val planner = ProjectPlanner()
        val project = EditorProjectDocument(
            assets = listOf(
                AssetRef(id = "v1", type = AssetType.VIDEO, uri = "content://demo/video1"),
                AssetRef(id = "a1", type = AssetType.AUDIO, uri = "content://demo/audio1"),
            ),
            videoTrack = listOf(
                VideoClipSpec(
                    assetId = "v1",
                    trimStartMs = 100,
                    trimEndMs = 4100,
                    effects = ClipEffectsSpec(overlayIds = listOf("logo")),
                ),
            ),
            audioTracks = listOf(
                AudioClipSpec(
                    assetId = "a1",
                    trimStartMs = 0,
                    trimEndMs = 1200,
                    loop = false,
                    volume = 0.5,
                ),
            ),
            overlays = listOf(
                OverlaySpec(
                    id = "logo",
                    uri = "content://demo/logo.png",
                    x = 0.8,
                    y = 0.2,
                    scale = 0.2,
                ),
            ),
            export = ExportSpec(outputUri = "content://demo/output.mp4"),
        )

        val planned = planner.plan(project).plannedProject
        assertNotNull(planned)
        assertEquals(4000L, planned.normalizedDurationMs)
        assertEquals(1, planned.audioLanes.size)
        assertEquals("a1", planned.audioLanes[0].assetId)
    }
}
