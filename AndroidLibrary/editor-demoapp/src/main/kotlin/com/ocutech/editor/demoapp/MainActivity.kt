package com.ocutech.editor.demoapp

import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import com.ocutech.editor.api.UnityVideoEditor
import com.ocutech.editor.model.JobStateResponse
import com.ocutech.editor.model.JsonCodec
import com.ocutech.editor.model.StartExportResponse

class MainActivity : AppCompatActivity() {
    private var currentJobId: String? = null
    private lateinit var output: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        output = findViewById(R.id.txtOutput)
        val btnInitialize = findViewById<Button>(R.id.btnInitialize)
        val btnStartExport = findViewById<Button>(R.id.btnStartExport)
        val btnPoll = findViewById<Button>(R.id.btnPoll)

        btnInitialize.setOnClickListener {
            appendResult("initialize", UnityVideoEditor.initialize("{}"))
        }

        btnStartExport.setOnClickListener {
            val responseJson = UnityVideoEditor.startExport(sampleProjectJson())
            appendResult("startExport", responseJson)
            val response = runCatching { JsonCodec.decode<StartExportResponse>(responseJson) }.getOrNull()
            currentJobId = response?.jobId
        }

        btnPoll.setOnClickListener {
            val jobId = currentJobId
            if (jobId == null) {
                appendLine("No jobId available. Run Start Export first.")
                return@setOnClickListener
            }
            val responseJson = UnityVideoEditor.getJobState(jobId)
            appendResult("getJobState", responseJson)
            val state = runCatching { JsonCodec.decode<JobStateResponse>(responseJson) }.getOrNull()?.state
            if (state != null && state.status.name in setOf("SUCCEEDED", "FAILED", "CANCELED")) {
                appendLine("Terminal state reached for job ${state.jobId}.")
            }
        }
    }

    private fun appendResult(action: String, json: String) {
        appendLine("[$action]")
        appendLine(json)
        appendLine("")
    }

    private fun appendLine(value: String) {
        output.append(value)
        output.append("\n")
    }

    private fun sampleProjectJson(): String = """
        {
          "version": 1,
          "assets": [
            { "id": "v1", "type": "video", "uri": "content://demo/video1" },
            { "id": "img1", "type": "image", "uri": "content://demo/image1" },
            { "id": "v2", "type": "video", "uri": "content://demo/video2" },
            { "id": "music1", "type": "audio", "uri": "content://demo/music1" }
          ],
          "videoTrack": [
            {
              "assetId": "v1",
              "trimStartMs": 0,
              "trimEndMs": 6000,
              "frameRate": 30,
              "removeAudio": false,
              "volume": 0.9,
              "effects": {
                "brightness": 0.05,
                "contrast": 1.05,
                "saturation": 1.0,
                "lut": "content://demo/lut_default.cube",
                "overlayIds": ["logo1"]
              },
              "transitionIn": { "type": "fade_from_black", "durationMs": 350 },
              "transitionOut": { "type": "dip_to_black", "durationMs": 350 }
            },
            {
              "assetId": "img1",
              "durationMs": 2500,
              "frameRate": 30,
              "removeAudio": true,
              "volume": 1.0,
              "effects": {
                "brightness": 0.02,
                "contrast": 1.0,
                "saturation": 1.1,
                "overlayIds": ["logo1"]
              },
              "transitionIn": { "type": "fade_from_black", "durationMs": 250 },
              "transitionOut": { "type": "dip_to_black", "durationMs": 250 }
            },
            {
              "assetId": "v2",
              "trimStartMs": 0,
              "trimEndMs": 6000,
              "frameRate": 30,
              "removeAudio": false,
              "volume": 0.9,
              "effects": {
                "brightness": 0.0,
                "contrast": 1.0,
                "saturation": 1.0,
                "overlayIds": ["logo1"]
              },
              "transitionIn": { "type": "fade_from_black", "durationMs": 350 },
              "transitionOut": { "type": "dip_to_black", "durationMs": 350 }
            }
          ],
          "audioTracks": [
            {
              "assetId": "music1",
              "trimStartMs": 0,
              "trimEndMs": 4000,
              "loop": true,
              "volume": 0.5
            }
          ],
          "overlays": [
            {
              "id": "logo1",
              "uri": "content://demo/logo.png",
              "x": 0.9,
              "y": 0.1,
              "scale": 0.2,
              "opacity": 0.9,
              "zIndex": 1
            }
          ],
          "export": {
            "outputUri": "content://demo/output.mp4",
            "videoMimeType": "video/avc",
            "audioMimeType": "audio/mp4a-latm",
            "width": 1920,
            "height": 1080,
            "fps": 30
          }
        }
    """.trimIndent()
}
