package com.ocutech.editor.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

@Serializable
data class ApiError(
    val code: String,
    val message: String,
    val causeClass: String? = null,
)

@Serializable
data class ValidationIssue(
    val code: String,
    val message: String,
    val field: String? = null,
)

@Serializable
data class ValidationReport(
    val errors: List<ValidationIssue> = emptyList(),
    val warnings: List<ValidationIssue> = emptyList(),
    val downgradedFeatures: List<String> = emptyList(),
    val normalizedDurationMs: Long = 0,
)

@Serializable
enum class JobStatus {
    @SerialName("queued")
    QUEUED,

    @SerialName("running")
    RUNNING,

    @SerialName("succeeded")
    SUCCEEDED,

    @SerialName("failed")
    FAILED,

    @SerialName("canceled")
    CANCELED,
}

@Serializable
data class JobStatePayload(
    val jobId: String,
    val status: JobStatus,
    val progressPercent: Int = 0,
    val outputUri: String? = null,
    val error: ApiError? = null,
    val warnings: List<ValidationIssue> = emptyList(),
)

@Serializable
data class InitializeResponse(
    val ok: Boolean,
    val libraryVersion: String,
    val schemaVersion: Int,
    val supportedFeatures: List<String> = emptyList(),
    val configStatus: String = "ready",
    val error: ApiError? = null,
)

@Serializable
data class ValidateProjectResponse(
    val ok: Boolean,
    val libraryVersion: String,
    val schemaVersion: Int,
    val report: ValidationReport,
    val error: ApiError? = null,
)

@Serializable
data class StartExportResponse(
    val ok: Boolean,
    val libraryVersion: String,
    val schemaVersion: Int,
    val jobId: String? = null,
    val accepted: Boolean,
    val initialState: JobStatus? = null,
    val error: ApiError? = null,
)

@Serializable
data class JobStateResponse(
    val ok: Boolean,
    val libraryVersion: String,
    val schemaVersion: Int,
    val state: JobStatePayload? = null,
    val error: ApiError? = null,
)

@Serializable
data class AckResponse(
    val ok: Boolean,
    val libraryVersion: String,
    val schemaVersion: Int,
    val acknowledged: Boolean,
    val message: String,
    val error: ApiError? = null,
)
