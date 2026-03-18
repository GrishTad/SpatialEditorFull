package com.ocutech.editor.export

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.ocutech.editor.core.PlannedProject
import com.ocutech.editor.model.AssetRef
import com.ocutech.editor.model.AssetType
import com.ocutech.editor.model.EditorProjectDocument
import com.ocutech.editor.model.ExportSpec
import com.ocutech.editor.model.JobStatus
import com.ocutech.editor.model.VideoClipSpec
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ExportJobManagerInstrumentedTest {
    @Test
    fun transitionsQueuedToSucceeded() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val executor = FakeExecutor(shouldSucceed = true)
        val manager = ExportJobManager(
            context = context,
            runtimeConfig = ExportRuntimeConfig(
                libraryVersion = "test",
                schemaVersion = 1,
                keepLastRequest = false,
            ),
            executor = executor,
        )

        val project = createProject()
        val jobId = manager.startExport("{}", project)
        assertNotNull(manager.getState(jobId))

        val terminal = awaitTerminalState(manager, jobId)
        assertEquals(JobStatus.SUCCEEDED, terminal.status)
        assertTrue((terminal.outputUri ?: "").contains("output.mp4"))
    }

    @Test
    fun cancelMovesJobToCanceled() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val executor = FakeExecutor(shouldSucceed = false, workDelayMs = 2_000L)
        val manager = ExportJobManager(
            context = context,
            runtimeConfig = ExportRuntimeConfig(
                libraryVersion = "test",
                schemaVersion = 1,
                keepLastRequest = false,
            ),
            executor = executor,
        )

        val project = createProject()
        val jobId = manager.startExport("{}", project)
        delay(150)
        assertTrue(manager.cancelJob(jobId))

        val terminal = awaitTerminalState(manager, jobId)
        assertEquals(JobStatus.CANCELED, terminal.status)
    }

    private suspend fun awaitTerminalState(manager: ExportJobManager, jobId: String): com.ocutech.editor.model.JobStatePayload {
        repeat(80) {
            val state = manager.getState(jobId)
            if (state != null && state.status in setOf(JobStatus.SUCCEEDED, JobStatus.FAILED, JobStatus.CANCELED)) {
                return state
            }
            delay(100)
        }
        error("Timed out waiting for terminal state")
    }

    private fun createProject(): EditorProjectDocument = EditorProjectDocument(
        assets = listOf(
            AssetRef(id = "v1", type = AssetType.VIDEO, uri = "content://demo/v1"),
        ),
        videoTrack = listOf(
            VideoClipSpec(
                assetId = "v1",
                trimStartMs = 0,
                trimEndMs = 2_000,
                removeAudio = false,
                volume = 1.0,
            ),
        ),
        export = ExportSpec(outputUri = "content://demo/output.mp4"),
    )

    private class FakeExecutor(
        private val shouldSucceed: Boolean,
        private val workDelayMs: Long = 400L,
    ) : ExportExecutor {
        @Volatile
        private var canceled = false

        override suspend fun export(
            jobId: String,
            plan: PlannedProject,
            onProgress: (Int) -> Unit,
        ): ExportOutput {
            var progress = 10
            while (progress < 90 && !canceled) {
                onProgress(progress)
                delay(50)
                progress += 20
            }
            delay(workDelayMs)
            if (canceled || !shouldSucceed) {
                throw kotlinx.coroutines.CancellationException("Canceled in fake executor.")
            }
            onProgress(100)
            return ExportOutput(outputUri = plan.source.export.outputUri)
        }

        override fun cancel(jobId: String) {
            canceled = true
        }
    }
}

