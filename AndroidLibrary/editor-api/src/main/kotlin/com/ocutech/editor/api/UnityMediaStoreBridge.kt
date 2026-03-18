package com.ocutech.editor.api

import android.app.Application
import android.content.ContentUris
import android.content.ContentValues
import android.content.Context
import android.os.Build
import android.provider.MediaStore
import com.ocutech.editor.model.ApiError
import com.ocutech.editor.model.JsonCodec
import kotlinx.serialization.Serializable
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object UnityMediaStoreBridge {
    @JvmStatic
    fun queryGalleryVideos(configJson: String): String {
        val context = resolveAppContext()
            ?: return JsonCodec.encode(
                GalleryQueryResponse(
                    ok = false,
                    error = ApiError(
                        code = "CONTEXT_UNAVAILABLE",
                        message = "Application context unavailable.",
                    ),
                ),
            )

        val config = runCatching {
            if (configJson.isBlank()) GalleryQueryConfig() else JsonCodec.decode<GalleryQueryConfig>(configJson)
        }.getOrElse { throwable ->
            return JsonCodec.encode(
                GalleryQueryResponse(
                    ok = false,
                    error = ApiError("INVALID_CONFIG_JSON", throwable.message ?: "Invalid config JSON."),
                ),
            )
        }

        return runCatching {
            val videos = queryVideos(context, config)
            JsonCodec.encode(GalleryQueryResponse(ok = true, videos = videos))
        }.getOrElse { throwable ->
            JsonCodec.encode(
                GalleryQueryResponse(
                    ok = false,
                    error = ApiError(
                        code = "GALLERY_QUERY_FAILED",
                        message = throwable.message ?: "Failed to query MediaStore videos.",
                        causeClass = throwable::class.java.name,
                    ),
                ),
            )
        }
    }

    @JvmStatic
    fun createOutputVideoUri(configJson: String): String {
        val context = resolveAppContext()
            ?: return JsonCodec.encode(
                CreateOutputUriResponse(
                    ok = false,
                    error = ApiError(
                        code = "CONTEXT_UNAVAILABLE",
                        message = "Application context unavailable.",
                    ),
                ),
            )

        val config = runCatching {
            if (configJson.isBlank()) CreateOutputUriConfig() else JsonCodec.decode<CreateOutputUriConfig>(configJson)
        }.getOrElse { throwable ->
            return JsonCodec.encode(
                CreateOutputUriResponse(
                    ok = false,
                    error = ApiError("INVALID_CONFIG_JSON", throwable.message ?: "Invalid config JSON."),
                ),
            )
        }

        return runCatching {
            val uri = insertOutputUri(context, config)
            JsonCodec.encode(CreateOutputUriResponse(ok = true, outputUri = uri.toString()))
        }.getOrElse { throwable ->
            JsonCodec.encode(
                CreateOutputUriResponse(
                    ok = false,
                    error = ApiError(
                        code = "CREATE_OUTPUT_URI_FAILED",
                        message = throwable.message ?: "Failed to create output uri.",
                        causeClass = throwable::class.java.name,
                    ),
                ),
            )
        }
    }

    private fun queryVideos(context: Context, config: GalleryQueryConfig): List<GalleryVideoEntry> {
        val projection = arrayOf(
            MediaStore.Video.Media._ID,
            MediaStore.Video.Media.DISPLAY_NAME,
            MediaStore.Video.Media.DURATION,
            MediaStore.Video.Media.SIZE,
            MediaStore.Video.Media.MIME_TYPE,
            MediaStore.Video.Media.DATE_ADDED,
        )

        val selection = mutableListOf<String>()
        val args = mutableListOf<String>()

        if (!config.bucketId.isNullOrBlank()) {
            selection += "${MediaStore.Video.Media.BUCKET_ID} = ?"
            args += config.bucketId
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && !config.includePending) {
            selection += "${MediaStore.Video.Media.IS_PENDING} = 0"
        }

        val where = if (selection.isEmpty()) null else selection.joinToString(" AND ")
        val orderBy = "${MediaStore.Video.Media.DATE_ADDED} DESC LIMIT ${config.limit.coerceIn(1, 1000)}"

        val result = mutableListOf<GalleryVideoEntry>()
        context.contentResolver.query(
            MediaStore.Video.Media.EXTERNAL_CONTENT_URI,
            projection,
            where,
            if (args.isEmpty()) null else args.toTypedArray(),
            orderBy,
        )?.use { cursor ->
            val idIndex = cursor.getColumnIndexOrThrow(MediaStore.Video.Media._ID)
            val nameIndex = cursor.getColumnIndexOrThrow(MediaStore.Video.Media.DISPLAY_NAME)
            val durationIndex = cursor.getColumnIndexOrThrow(MediaStore.Video.Media.DURATION)
            val sizeIndex = cursor.getColumnIndexOrThrow(MediaStore.Video.Media.SIZE)
            val mimeIndex = cursor.getColumnIndexOrThrow(MediaStore.Video.Media.MIME_TYPE)
            val dateAddedIndex = cursor.getColumnIndexOrThrow(MediaStore.Video.Media.DATE_ADDED)

            while (cursor.moveToNext()) {
                val id = cursor.getLong(idIndex)
                val uri = ContentUris.withAppendedId(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, id)
                result += GalleryVideoEntry(
                    uri = uri.toString(),
                    displayName = cursor.getString(nameIndex) ?: "video_$id",
                    durationMs = cursor.getLong(durationIndex),
                    sizeBytes = cursor.getLong(sizeIndex),
                    mimeType = cursor.getString(mimeIndex) ?: "video/*",
                    dateAddedSec = cursor.getLong(dateAddedIndex),
                )
            }
        }
        return result
    }

    private fun insertOutputUri(context: Context, config: CreateOutputUriConfig): android.net.Uri {
        val timestamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
        val displayName = (config.displayName?.takeIf { it.isNotBlank() } ?: "SpatialEditor_$timestamp")
            .let { if (it.endsWith(".mp4", true)) it else "$it.mp4" }

        val values = ContentValues().apply {
            put(MediaStore.Video.Media.DISPLAY_NAME, displayName)
            put(MediaStore.Video.Media.MIME_TYPE, config.mimeType)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                put(MediaStore.Video.Media.RELATIVE_PATH, config.relativePath)
                put(MediaStore.Video.Media.IS_PENDING, 0)
            }
        }

        return context.contentResolver.insert(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, values)
            ?: throw IllegalStateException("MediaStore insert returned null uri.")
    }

    private fun resolveAppContext(): Context? = try {
        val activityThread = Class.forName("android.app.ActivityThread")
        val app = activityThread.getMethod("currentApplication").invoke(null) as? Application
        app?.applicationContext
    } catch (_: Throwable) {
        null
    }
}

@Serializable
data class GalleryQueryConfig(
    val limit: Int = 200,
    val bucketId: String? = null,
    val includePending: Boolean = false,
)

@Serializable
data class GalleryVideoEntry(
    val uri: String,
    val displayName: String,
    val durationMs: Long,
    val sizeBytes: Long,
    val mimeType: String,
    val dateAddedSec: Long,
)

@Serializable
data class GalleryQueryResponse(
    val ok: Boolean,
    val videos: List<GalleryVideoEntry> = emptyList(),
    val error: ApiError? = null,
)

@Serializable
data class CreateOutputUriConfig(
    val displayName: String? = null,
    val mimeType: String = "video/mp4",
    val relativePath: String = "Movies/SpatialEditor",
)

@Serializable
data class CreateOutputUriResponse(
    val ok: Boolean,
    val outputUri: String? = null,
    val error: ApiError? = null,
)
