package com.ocutech.editor.export

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.ocutech.editor.model.AssetRef
import com.ocutech.editor.model.AssetType
import com.ocutech.editor.model.EditorProjectDocument
import com.ocutech.editor.model.ExportSpec
import com.ocutech.editor.model.VideoClipSpec
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

@RunWith(AndroidJUnit4::class)
class ProjectUriAccessValidatorInstrumentedTest {
    @Test
    fun validatesReadableAndWritableFileUris() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val input = File(context.cacheDir, "validator_input.mp4")
        input.writeBytes(byteArrayOf(0x00, 0x01, 0x02))
        val output = File(context.cacheDir, "validator_output.mp4")
        if (output.exists()) {
            output.delete()
        }

        val project = EditorProjectDocument(
            assets = listOf(
                AssetRef(id = "v1", type = AssetType.VIDEO, uri = input.absolutePath),
            ),
            videoTrack = listOf(
                VideoClipSpec(
                    assetId = "v1",
                    trimStartMs = 0,
                    trimEndMs = 1000,
                ),
            ),
            export = ExportSpec(outputUri = output.absolutePath),
        )

        val issues = ProjectUriAccessValidator.validate(context, project)
        assertTrue(issues.isEmpty())
    }
}

