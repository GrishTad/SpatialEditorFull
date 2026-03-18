package com.ocutech.editor.api

import android.app.Application
import android.content.Context
import com.ocutech.editor.core.ProjectPlanner
import com.ocutech.editor.export.ExportJobManager
import com.ocutech.editor.export.ProjectUriAccessValidator
import com.ocutech.editor.export.ExportRuntimeConfig
import com.ocutech.editor.model.AckResponse
import com.ocutech.editor.model.ApiError
import com.ocutech.editor.model.EditorConfig
import com.ocutech.editor.model.InitializeResponse
import com.ocutech.editor.model.JobStateResponse
import com.ocutech.editor.model.JsonCodec
import com.ocutech.editor.model.StartExportResponse
import com.ocutech.editor.model.ValidateProjectResponse

object UnityVideoEditor {
    private const val LIBRARY_VERSION = "0.1.1-threadfix"
    private const val SCHEMA_VERSION = 1
    private val lock = Any()

    @Volatile
    private var runtime: Runtime? = null

    @JvmStatic
    fun initialize(configJson: String): String {
        val config = runCatching {
            if (configJson.isBlank()) EditorConfig() else JsonCodec.decode<EditorConfig>(configJson)
        }.getOrElse { t ->
            return JsonCodec.encode(
                InitializeResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    configStatus = "invalid_config",
                    error = ApiError("INVALID_CONFIG_JSON", t.message ?: "Invalid config JSON.", t::class.java.name),
                ),
            )
        }

        val context = resolveAppContext()
        if (context == null) {
            return JsonCodec.encode(
                InitializeResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    configStatus = "context_unavailable",
                    error = ApiError(
                        code = "CONTEXT_UNAVAILABLE",
                        message = "Application context could not be resolved. Call initialize after Android runtime is ready.",
                    ),
                ),
            )
        }

        synchronized(lock) {
            runtime = Runtime(
                config = config,
                manager = ExportJobManager(
                    context = context,
                    runtimeConfig = ExportRuntimeConfig(
                        libraryVersion = LIBRARY_VERSION,
                        schemaVersion = SCHEMA_VERSION,
                        keepLastRequest = config.keepLastRequest,
                        debugLogging = config.debugLogging,
                    ),
                ),
                planner = ProjectPlanner(),
                context = context,
            )
        }

        return JsonCodec.encode(
            InitializeResponse(
                ok = true,
                libraryVersion = LIBRARY_VERSION,
                schemaVersion = SCHEMA_VERSION,
                supportedFeatures = SUPPORTED_FEATURES,
                configStatus = "ready",
            ),
        )
    }

    @JvmStatic
    fun validateProject(projectJson: String): String {
        val runtimeState = ensureRuntime()
        if (runtimeState == null) {
            return notInitializedValidateResponse()
        }

        val project = parseProject(projectJson) ?: return invalidProjectValidateResponse()
        val plannerReport = runtimeState.planner.validate(project)
        val uriIssues = ProjectUriAccessValidator.validate(runtimeState.context, project)
        val report = plannerReport.copy(errors = plannerReport.errors + uriIssues)
        return JsonCodec.encode(
            ValidateProjectResponse(
                ok = report.errors.isEmpty(),
                libraryVersion = LIBRARY_VERSION,
                schemaVersion = SCHEMA_VERSION,
                report = report,
            ),
        )
    }

    @JvmStatic
    fun startExport(projectJson: String): String {
        val runtimeState = ensureRuntime()
            ?: return JsonCodec.encode(
                StartExportResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    accepted = false,
                    error = ApiError("NOT_INITIALIZED", "Call initialize() before startExport()."),
                ),
            )

        val project = parseProject(projectJson)
            ?: return JsonCodec.encode(
                StartExportResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    accepted = false,
                    error = ApiError("INVALID_PROJECT_JSON", "Project JSON is malformed."),
                ),
            )

        val uriIssues = ProjectUriAccessValidator.validate(runtimeState.context, project)
        if (uriIssues.isNotEmpty()) {
            return JsonCodec.encode(
                StartExportResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    accepted = false,
                    error = ApiError(
                        code = "PROJECT_URI_VALIDATION_FAILED",
                        message = uriIssues.first().message,
                    ),
                ),
            )
        }

        val jobId = runtimeState.manager.startExport(projectJson, project)
        return JsonCodec.encode(
            StartExportResponse(
                ok = true,
                libraryVersion = LIBRARY_VERSION,
                schemaVersion = SCHEMA_VERSION,
                jobId = jobId,
                accepted = true,
                initialState = com.ocutech.editor.model.JobStatus.QUEUED,
            ),
        )
    }

    @JvmStatic
    fun getJobState(jobId: String): String {
        val runtimeState = ensureRuntime()
            ?: return JsonCodec.encode(
                JobStateResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    error = ApiError("NOT_INITIALIZED", "Call initialize() before getJobState()."),
                ),
            )

        val state = runtimeState.manager.getState(jobId)
        return if (state == null) {
            JsonCodec.encode(
                JobStateResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    error = ApiError("JOB_NOT_FOUND", "No job found for id '$jobId'."),
                ),
            )
        } else {
            JsonCodec.encode(
                JobStateResponse(
                    ok = true,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    state = state,
                ),
            )
        }
    }

    @JvmStatic
    fun cancelJob(jobId: String): String {
        val runtimeState = ensureRuntime()
            ?: return JsonCodec.encode(
                AckResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    acknowledged = false,
                    message = "Call initialize() before cancelJob().",
                    error = ApiError("NOT_INITIALIZED", "Call initialize() before cancelJob()."),
                ),
            )

        val acknowledged = runtimeState.manager.cancelJob(jobId)
        return JsonCodec.encode(
            AckResponse(
                ok = acknowledged,
                libraryVersion = LIBRARY_VERSION,
                schemaVersion = SCHEMA_VERSION,
                acknowledged = acknowledged,
                message = if (acknowledged) "Cancellation requested." else "Job not found.",
                error = if (acknowledged) null else ApiError("JOB_NOT_FOUND", "No job found for id '$jobId'."),
            ),
        )
    }

    @JvmStatic
    fun releaseJob(jobId: String): String {
        val runtimeState = ensureRuntime()
            ?: return JsonCodec.encode(
                AckResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    acknowledged = false,
                    message = "Call initialize() before releaseJob().",
                    error = ApiError("NOT_INITIALIZED", "Call initialize() before releaseJob()."),
                ),
            )

        val acknowledged = runtimeState.manager.releaseJob(jobId)
        return JsonCodec.encode(
            AckResponse(
                ok = acknowledged,
                libraryVersion = LIBRARY_VERSION,
                schemaVersion = SCHEMA_VERSION,
                acknowledged = acknowledged,
                message = if (acknowledged) "Job released." else "Job not found.",
                error = if (acknowledged) null else ApiError("JOB_NOT_FOUND", "No job found for id '$jobId'."),
            ),
        )
    }

    @JvmStatic
    fun releaseAll(): String {
        val runtimeState = ensureRuntime()
            ?: return JsonCodec.encode(
                AckResponse(
                    ok = false,
                    libraryVersion = LIBRARY_VERSION,
                    schemaVersion = SCHEMA_VERSION,
                    acknowledged = false,
                    message = "Call initialize() before releaseAll().",
                    error = ApiError("NOT_INITIALIZED", "Call initialize() before releaseAll()."),
                ),
            )

        val released = runtimeState.manager.releaseAll()
        return JsonCodec.encode(
            AckResponse(
                ok = true,
                libraryVersion = LIBRARY_VERSION,
                schemaVersion = SCHEMA_VERSION,
                acknowledged = true,
                message = "Released $released job(s).",
            ),
        )
    }

    private fun ensureRuntime(): Runtime? = runtime ?: synchronized(lock) {
        runtime ?: run {
            val context = resolveAppContext() ?: return@synchronized null
            Runtime(
                config = EditorConfig(),
                manager = ExportJobManager(
                    context = context,
                    runtimeConfig = ExportRuntimeConfig(
                        libraryVersion = LIBRARY_VERSION,
                        schemaVersion = SCHEMA_VERSION,
                    ),
                ),
                planner = ProjectPlanner(),
                context = context,
            ).also { runtime = it }
        }
    }

    private fun parseProject(projectJson: String) = runCatching {
        JsonCodec.decode<com.ocutech.editor.model.EditorProjectDocument>(projectJson)
    }.getOrNull()

    private fun notInitializedValidateResponse(): String = JsonCodec.encode(
        ValidateProjectResponse(
            ok = false,
            libraryVersion = LIBRARY_VERSION,
            schemaVersion = SCHEMA_VERSION,
            report = com.ocutech.editor.model.ValidationReport(),
            error = ApiError("NOT_INITIALIZED", "Call initialize() before validateProject()."),
        ),
    )

    private fun invalidProjectValidateResponse(): String = JsonCodec.encode(
        ValidateProjectResponse(
            ok = false,
            libraryVersion = LIBRARY_VERSION,
            schemaVersion = SCHEMA_VERSION,
            report = com.ocutech.editor.model.ValidationReport(
                errors = listOf(
                    com.ocutech.editor.model.ValidationIssue(
                        code = "INVALID_PROJECT_JSON",
                        message = "Project JSON is malformed.",
                    ),
                ),
            ),
            error = ApiError("INVALID_PROJECT_JSON", "Project JSON is malformed."),
        ),
    )

    private fun resolveAppContext(): Context? = try {
        val activityThread = Class.forName("android.app.ActivityThread")
        val app = activityThread.getMethod("currentApplication").invoke(null) as? Application
        app?.applicationContext
    } catch (_: Throwable) {
        null
    }

    private data class Runtime(
        val config: EditorConfig,
        val manager: ExportJobManager,
        val planner: ProjectPlanner,
        val context: Context,
    )

    private val SUPPORTED_FEATURES = listOf(
        "sequential_video_image_timeline",
        "clip_trim",
        "clip_mute_and_volume",
        "background_music_lane",
        "color_controls",
        "lut_pipeline",
        "overlay_watermark_sticker",
        "dip_to_black_and_fade_from_black",
        "media_store_gallery_bridge",
        "multi_sequence_composition",
        "job_progress_polling",
        "structured_errors",
    )
}
