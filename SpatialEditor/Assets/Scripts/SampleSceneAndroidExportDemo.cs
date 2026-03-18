using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Video;

public class SampleSceneAndroidExportDemo : MonoBehaviour
{
    private const string ReadMediaVideoPermission = "android.permission.READ_MEDIA_VIDEO";

    [Header("Demo Flow")]
    [SerializeField] private bool runOnStart = true;
    [SerializeField] private float pollIntervalSeconds = 0.5f;
    [SerializeField] private int galleryLimit = 200;
    [SerializeField] private string outputRelativePath = "Movies/SpatialEditor";

    [Header("Scene Links")]
    [SerializeField] private VRVideoSurfaceController surfaceController;
    [SerializeField] private VideoPlayer videoPlayer;

    private void Awake()
    {
        if (surfaceController == null)
        {
            surfaceController = FindAnyObjectByType<VRVideoSurfaceController>();
        }

        if (videoPlayer == null)
        {
            videoPlayer = FindAnyObjectByType<VideoPlayer>();
        }

        if (surfaceController != null && videoPlayer != null)
        {
            surfaceController.SetVideoSource(videoPlayer);
        }
    }

    private void Start()
    {
        if (runOnStart)
        {
            StartCoroutine(RunExportDemo());
        }
    }

    public IEnumerator RunExportDemo()
    {
        if (!AndroidVideoEditorBridge.IsAndroidRuntime)
        {
            Debug.LogWarning("SampleSceneAndroidExportDemo: Android runtime only.");
            yield break;
        }

        yield return EnsureGalleryPermission();

        string initJson = AndroidVideoEditorBridge.Initialize("{}");
        Debug.Log("[AndroidBridge] initialize => " + initJson);

        string queryJson = AndroidVideoEditorBridge.QueryGalleryVideos(new AndroidGalleryQueryConfig
        {
            limit = Mathf.Clamp(galleryLimit, 1, 1000),
            includePending = false,
        });
        Debug.Log("[AndroidBridge] queryGalleryVideos => " + queryJson);

        AndroidGalleryQueryResponse gallery = JsonUtility.FromJson<AndroidGalleryQueryResponse>(queryJson);
        if (gallery == null || !gallery.ok || gallery.videos == null || gallery.videos.Length < 2)
        {
            Debug.LogError("SampleSceneAndroidExportDemo: Need at least 2 gallery videos for demo export.");
            yield break;
        }

        AndroidGalleryVideoEntry first = gallery.videos[0];
        AndroidGalleryVideoEntry second = gallery.videos[1];
        AndroidGalleryVideoEntry[] selectedInputs = new[] { first, second };

        string createOutputJson = AndroidVideoEditorBridge.CreateOutputVideoUri(new AndroidCreateOutputUriConfig
        {
            displayName = "SpatialEditor_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            relativePath = outputRelativePath,
            mimeType = "video/mp4",
        });
        Debug.Log("[AndroidBridge] createOutputVideoUri => " + createOutputJson);

        AndroidCreateOutputUriResponse output = JsonUtility.FromJson<AndroidCreateOutputUriResponse>(createOutputJson);
        if (output == null || !output.ok || string.IsNullOrEmpty(output.outputUri))
        {
            Debug.LogError("SampleSceneAndroidExportDemo: Failed to create output URI.");
            yield break;
        }

        AndroidProjectDocument project = BuildProject(selectedInputs, output.outputUri);
        string projectJson = JsonUtility.ToJson(project);

        string validateJson = AndroidVideoEditorBridge.ValidateProject(projectJson);
        Debug.Log("[AndroidBridge] validateProject => " + validateJson);

        string startJson = AndroidVideoEditorBridge.StartExport(projectJson);
        Debug.Log("[AndroidBridge] startExport => " + startJson);

        AndroidStartExportResponse start = JsonUtility.FromJson<AndroidStartExportResponse>(startJson);
        if (start == null || !start.ok || !start.accepted || string.IsNullOrEmpty(start.jobId))
        {
            Debug.LogError("SampleSceneAndroidExportDemo: Export start failed.");
            yield break;
        }

        while (true)
        {
            string stateJson = AndroidVideoEditorBridge.GetJobState(start.jobId);
            if (AndroidJobPolling.TryParseJobState(stateJson, out AndroidJobStateResponse state))
            {
                Debug.Log($"[AndroidBridge] job={state.state.jobId} status={state.state.status} progress={state.state.progressPercent}%");

                string status = state.state.status;
                if (status != null && status.ToLowerInvariant() == "succeeded")
                {
                    OnExportSucceeded(first, state.state.outputUri);
                    break;
                }

                if (AndroidJobPolling.IsTerminalStatus(status))
                {
                    Debug.LogError("SampleSceneAndroidExportDemo: Export ended in terminal status: " + stateJson);
                    break;
                }
            }
            else
            {
                Debug.LogWarning("SampleSceneAndroidExportDemo: Failed to parse job state: " + stateJson);
            }

            yield return new WaitForSeconds(pollIntervalSeconds);
        }

        AndroidVideoEditorBridge.ReleaseJob(start.jobId);
    }

    private void OnExportSucceeded(AndroidGalleryVideoEntry firstInput, string outputUri)
    {
        if (surfaceController != null)
        {
            FilenameVideoDetectionResult detection = FilenameVideoTypeDetector.Detect(firstInput.displayName);
            surfaceController.ApplyDetection(detection);
        }

        if (videoPlayer != null && !string.IsNullOrEmpty(outputUri))
        {
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = outputUri;
            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = false;
            videoPlayer.Prepare();
            videoPlayer.Play();
            Debug.Log("SampleSceneAndroidExportDemo: Playing exported output => " + outputUri);
        }
    }

    private AndroidProjectDocument BuildProject(AndroidGalleryVideoEntry[] inputs, string outputUri)
    {
        List<AndroidAssetRef> assets = new List<AndroidAssetRef>();
        List<AndroidVideoClipSpec> clips = new List<AndroidVideoClipSpec>();

        for (int i = 0; i < inputs.Length; i++)
        {
            AndroidGalleryVideoEntry input = inputs[i];
            string assetId = "v" + (i + 1);
            long endMs = input.durationMs > 0 ? input.durationMs : 6000;
            assets.Add(new AndroidAssetRef
            {
                id = assetId,
                type = "video",
                uri = input.uri,
            });
            clips.Add(new AndroidVideoClipSpec
            {
                assetId = assetId,
                trimStartMs = 0,
                trimEndMs = endMs,
                frameRate = 30,
                removeAudio = false,
                volume = 1.0,
                effects = new AndroidClipEffectsSpec
                {
                    brightness = i == 0 ? 0.05 : 0.0,
                    contrast = i == 0 ? 1.05 : 1.0,
                    saturation = 1.0,
                    overlayIds = new string[0],
                },
                transitionIn = new AndroidTransitionSpec { type = "fade_from_black", durationMs = 250 },
                transitionOut = new AndroidTransitionSpec { type = "dip_to_black", durationMs = 250 },
            });
        }

        return new AndroidProjectDocument
        {
            version = 1,
            assets = assets.ToArray(),
            videoTrack = clips.ToArray(),
            audioTracks = new AndroidAudioClipSpec[0],
            overlays = new AndroidOverlaySpec[0],
            export = new AndroidExportSpec
            {
                outputUri = outputUri,
                videoMimeType = "video/avc",
                audioMimeType = "audio/mp4a-latm",
                width = 1920,
                height = 1080,
                fps = 30,
            },
        };
    }

    private IEnumerator EnsureGalleryPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        int sdkInt = 0;
        using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            sdkInt = version.GetStatic<int>("SDK_INT");
        }

        if (sdkInt >= 33)
        {
            if (!Permission.HasUserAuthorizedPermission(ReadMediaVideoPermission))
            {
                Permission.RequestUserPermission(ReadMediaVideoPermission);
                yield return new WaitForSeconds(0.5f);
            }
        }
        else
        {
            if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            {
                Permission.RequestUserPermission(Permission.ExternalStorageRead);
                yield return new WaitForSeconds(0.5f);
            }
        }
#else
        yield return null;
#endif
    }
}
