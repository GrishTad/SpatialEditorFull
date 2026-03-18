package com.ocutech.editor.export

import android.content.Context
import android.net.Uri
import com.ocutech.editor.model.EditorProjectDocument
import com.ocutech.editor.model.ValidationIssue
import java.io.File

object ProjectUriAccessValidator {
    fun validate(context: Context, project: EditorProjectDocument): List<ValidationIssue> {
        val issues = mutableListOf<ValidationIssue>()

        project.assets.forEachIndexed { index, asset ->
            val parsed = parseUri(asset.uri)
            if (parsed == null) {
                issues += ValidationIssue(
                    code = "INVALID_ASSET_URI",
                    message = "Asset uri is invalid.",
                    field = "assets[$index].uri",
                )
                return@forEachIndexed
            }

            val readable = when (parsed.scheme?.lowercase()) {
                "content" -> canReadContentUri(context, parsed)
                "file" -> canReadFile(parsed.path)
                null, "" -> canReadFile(asset.uri)
                else -> false
            }

            if (!readable) {
                issues += ValidationIssue(
                    code = "UNREADABLE_ASSET_URI",
                    message = "Asset uri cannot be read from Android runtime.",
                    field = "assets[$index].uri",
                )
            }
        }

        val outputUri = parseUri(project.export.outputUri)
        if (outputUri == null) {
            issues += ValidationIssue(
                code = "INVALID_OUTPUT_URI",
                message = "export.outputUri is invalid.",
                field = "export.outputUri",
            )
            return issues
        }

        val writable = when (outputUri.scheme?.lowercase()) {
            "content" -> canWriteContentUri(context, outputUri)
            "file" -> canWriteFile(outputUri.path)
            null, "" -> canWriteFile(project.export.outputUri)
            else -> false
        }

        if (!writable) {
            issues += ValidationIssue(
                code = "UNWRITABLE_OUTPUT_URI",
                message = "export.outputUri is not writable.",
                field = "export.outputUri",
            )
        }

        return issues
    }

    private fun parseUri(value: String): Uri? = runCatching { Uri.parse(value) }.getOrNull()

    private fun canReadContentUri(context: Context, uri: Uri): Boolean = runCatching {
        context.contentResolver.openInputStream(uri)?.use { true } ?: false
    }.getOrDefault(false)

    private fun canReadFile(path: String?): Boolean {
        if (path.isNullOrBlank()) return false
        val file = File(path)
        return file.exists() && file.canRead()
    }

    private fun canWriteContentUri(context: Context, uri: Uri): Boolean = runCatching {
        context.contentResolver.openFileDescriptor(uri, "rw")?.use { true } ?: false
    }.getOrDefault(false)

    private fun canWriteFile(path: String?): Boolean {
        if (path.isNullOrBlank()) return false
        val file = File(path)
        val parent = file.parentFile
        if (parent != null && !parent.exists()) {
            parent.mkdirs()
        }
        return (parent == null || parent.canWrite())
    }
}
