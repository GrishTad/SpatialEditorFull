package com.ocutech.editor.export

import android.content.Context
import com.ocutech.editor.core.ProjectPlanner
import com.ocutech.editor.model.ApiError
import com.ocutech.editor.model.EditorProjectDocument
import com.ocutech.editor.model.JobStatePayload
import com.ocutech.editor.model.JobStatus
import com.ocutech.editor.model.ValidationIssue
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

class ExportJobManager(
    private val context: Context,
    private val runtimeConfig: ExportRuntimeConfig,
    private val planner: ProjectPlanner = ProjectPlanner(),
    private val executor: ExportExecutor = Media3ExportExecutor(context, runtimeConfig),
    dispatcher: CoroutineDispatcher = Dispatchers.Default,
) {
    private val scope = CoroutineScope(SupervisorJob() + dispatcher)
    private val jobs = ConcurrentHashMap<String, ManagedJob>()

    fun startExport(projectJson: String, project: EditorProjectDocument): String {
        if (runtimeConfig.keepLastRequest) {
            context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .edit()
                .putString(KEY_LAST_PROJECT_JSON, projectJson)
                .apply()
        }

        val jobId = UUID.randomUUID().toString()
        val initial = JobStatePayload(jobId = jobId, status = JobStatus.QUEUED)

        val task = scope.launch {
            runExport(jobId, project)
        }
        jobs[jobId] = ManagedJob(snapshot = initial, task = task)
        return jobId
    }

    fun getState(jobId: String): JobStatePayload? = jobs[jobId]?.snapshot

    fun cancelJob(jobId: String): Boolean {
        val managed = jobs[jobId] ?: return false
        if (managed.snapshot.status in TERMINAL_STATUSES) {
            return true
        }
        executor.cancel(jobId)
        managed.task.cancel(CancellationException("Canceled by client request"))
        update(jobId) { old ->
            if (old.status in TERMINAL_STATUSES) old else old.copy(status = JobStatus.CANCELED)
        }
        return true
    }

    fun releaseJob(jobId: String): Boolean {
        val removed = jobs.remove(jobId) ?: return false
        if (removed.snapshot.status !in TERMINAL_STATUSES) {
            removed.task.cancel(CancellationException("Released by client"))
        }
        return true
    }

    fun releaseAll(): Int {
        val ids = jobs.keys.toList()
        ids.forEach { releaseJob(it) }
        return ids.size
    }

    private suspend fun runExport(
        jobId: String,
        project: EditorProjectDocument,
    ) {
        try {
            update(jobId) { it.copy(status = JobStatus.RUNNING, progressPercent = 1) }
            val planning = planner.plan(project)

            if (planning.report.errors.isNotEmpty()) {
                update(jobId) {
                    it.copy(
                        status = JobStatus.FAILED,
                        progressPercent = 100,
                        warnings = planning.report.warnings,
                        error = ApiError(
                            code = "VALIDATION_FAILED",
                            message = "Project validation failed.",
                        ),
                    )
                }
                return
            }

            update(jobId) { it.copy(warnings = planning.report.warnings, progressPercent = 5) }

            val plannedProject = requireNotNull(planning.plannedProject)
            val output = executor.export(jobId, plannedProject) { progress ->
                update(jobId) { current ->
                    current.copy(
                        status = JobStatus.RUNNING,
                        progressPercent = progress.coerceIn(0, 100),
                    )
                }
            }

            val mergedWarnings = planning.report.warnings + output.warnings
            update(jobId) {
                it.copy(
                    status = JobStatus.SUCCEEDED,
                    progressPercent = 100,
                    outputUri = output.outputUri,
                    warnings = mergedWarnings,
                )
            }
        } catch (_: CancellationException) {
            update(jobId) { old ->
                if (old.status in TERMINAL_STATUSES) old else old.copy(status = JobStatus.CANCELED)
            }
        } catch (t: Throwable) {
            update(jobId) {
                it.copy(
                    status = JobStatus.FAILED,
                    progressPercent = 100,
                    error = ApiError(
                        code = "EXPORT_RUNTIME_ERROR",
                        message = t.message ?: "Unexpected export error.",
                        causeClass = t::class.java.name,
                    ),
                )
            }
        }
    }

    private fun update(
        jobId: String,
        mutate: (JobStatePayload) -> JobStatePayload,
    ) {
        val managed = jobs[jobId] ?: return
        synchronized(managed) {
            managed.snapshot = mutate(managed.snapshot)
        }
    }

    fun lastProjectJson(): String? = context
        .getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        .getString(KEY_LAST_PROJECT_JSON, null)

    private class ManagedJob(
        @Volatile var snapshot: JobStatePayload,
        val task: Job,
    )

    private companion object {
        private const val PREFS = "unity_editor_state"
        private const val KEY_LAST_PROJECT_JSON = "last_project_json"
        private val TERMINAL_STATUSES = setOf(JobStatus.SUCCEEDED, JobStatus.FAILED, JobStatus.CANCELED)
    }
}
