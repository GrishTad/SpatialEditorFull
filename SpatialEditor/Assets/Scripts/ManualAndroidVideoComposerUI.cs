using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Video;

public class ManualAndroidVideoComposerUI : MonoBehaviour
{
    [Serializable]
    public class PlaneBinding
    {
        [Tooltip("Projection key: Flat3D, Flat, VR180, VR190, VR200, VR220, VR360")]
        public string projectionKey = "Flat3D";
        public GameObject planeObject;
        public VideoPlayer videoPlayer;
    }

    [Header("UI")]
    [SerializeField] private Button pickTwoVideosButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private GameObject statusPopupObject;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private bool hidePopupWhenIdle;

    [Header("Plane Bindings")]
    [Tooltip("Add one row per plane you want to control.")]
    [SerializeField] private PlaneBinding[] planeBindings;

    [Header("Export")]
    [SerializeField] private float pollIntervalSeconds = 0.5f;
    [SerializeField] private int outputWidth = 1920;
    [SerializeField] private int outputHeight = 1080;
    [SerializeField] private int outputFps = 30;
    [SerializeField] private bool setAllUnmatchedPlanesInactive = true;

    private AndroidGalleryVideoEntry firstVideo;
    private AndroidGalleryVideoEntry secondVideo;
    private bool busy;
    private bool editorBridgeInitialized;
    private string lastOutputPath;

    public string LastOutputPath => lastOutputPath;

    private void Awake()
    {
        if (pickTwoVideosButton != null)
        {
            pickTwoVideosButton.onClick.AddListener(OnPickTwoVideosButtonClicked);
        }

        if (saveButton != null)
        {
            saveButton.onClick.AddListener(OnSaveButtonClicked);
        }

        if (hidePopupWhenIdle)
        {
            SetPopupVisible(false);
        }
    }

    private void OnDestroy()
    {
        if (pickTwoVideosButton != null)
        {
            pickTwoVideosButton.onClick.RemoveListener(OnPickTwoVideosButtonClicked);
        }

        if (saveButton != null)
        {
            saveButton.onClick.RemoveListener(OnSaveButtonClicked);
        }
    }

    public void OnPickTwoVideosButtonClicked()
    {
        if (!busy)
        {
            StartCoroutine(PickTwoVideosFlow());
        }
    }

    public void OnSaveButtonClicked()
    {
        if (!busy)
        {
            StartCoroutine(ExportFlow());
        }
    }

    private IEnumerator PickTwoVideosFlow()
    {
        busy = true;
        SetButtonsInteractable(false);
        SetStatus("Processing: starting video selection...", true);

        if (!AndroidVideoEditorBridge.IsAndroidRuntime)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: Android runtime only.");
            SetStatus("Failed: Android runtime only.", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        SetStatus("Processing: requesting gallery permission...", true);
        AndroidGalleryVideoEntry[] picked = null;
        yield return PickTwoVideosWithNativeGallery((result) => picked = result);

        if (picked == null || picked.Length < 2)
        {
            Debug.LogError("ManualAndroidVideoComposerUI: Could not select 2 videos.");
            SetStatus("Failed: could not select 2 videos.", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        firstVideo = picked[0];
        secondVideo = picked[1];

        ApplyVideoToMatchingPlane(firstVideo, 0);
        ApplyVideoToMatchingPlane(secondVideo, 1);

        Debug.Log($"ManualAndroidVideoComposerUI: Selected videos => {firstVideo.displayName} | {secondVideo.displayName}");
        SetStatus($"Finished: selected videos\n1) {firstVideo.displayName}\n2) {secondVideo.displayName}", true);

        busy = false;
        SetButtonsInteractable(true);
    }

    private IEnumerator ExportFlow()
    {
        busy = true;
        SetButtonsInteractable(false);
        SetStatus("Processing: preparing export...", true);

        if (!AndroidVideoEditorBridge.IsAndroidRuntime)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: Android runtime only.");
            SetStatus("Failed: Android runtime only.", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        if (firstVideo == null || secondVideo == null)
        {
            Debug.LogError("ManualAndroidVideoComposerUI: Pick 2 videos before exporting.");
            SetStatus("Failed: pick 2 videos before exporting.", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        if (!editorBridgeInitialized)
        {
            SetStatus("Processing: initializing editor bridge...", true);
            string initJson = AndroidVideoEditorBridge.Initialize("{}");
            Debug.Log("ManualAndroidVideoComposerUI: initialize => " + initJson);
            AndroidInitializeResponse init = SafeFromJson<AndroidInitializeResponse>(initJson);
            if (init == null || !init.ok)
            {
                Debug.LogError("ManualAndroidVideoComposerUI: initialize failed => " + initJson);
                SetStatus("Failed: editor initialization failed.", true);
                busy = false;
                SetButtonsInteractable(true);
                yield break;
            }

            editorBridgeInitialized = true;
        }

        string outputFileName = BuildOutputFileName(firstVideo.displayName, secondVideo.displayName);
        string outputPath = Path.Combine(Application.persistentDataPath, outputFileName);
        string outputUri = "file://" + outputPath.Replace("\\", "/");
        Debug.Log("ManualAndroidVideoComposerUI: outputPath => " + outputPath);
        Debug.Log("ManualAndroidVideoComposerUI: outputUri => " + outputUri);
        SetStatus("Processing: output path prepared...\n" + outputPath, true);

        string projectJson = BuildProjectJson(new[] { firstVideo, secondVideo }, outputUri);
        Debug.Log("ManualAndroidVideoComposerUI: projectJson => " + projectJson);

        SetStatus("Processing: validating project...", true);
        string validateJson = AndroidVideoEditorBridge.ValidateProject(projectJson);
        AndroidValidateResponse validate = SafeFromJson<AndroidValidateResponse>(validateJson);
        if (validate == null || !validate.ok)
        {
            Debug.LogError("ManualAndroidVideoComposerUI: validateProject failed => " + validateJson);
            SetStatus("Failed: project validation failed.\nOutput path:\n" + outputPath, true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        SetStatus("Processing: starting export job...", true);
        AndroidVideoEditorBridge.ReleaseAll();
        string startJson = AndroidVideoEditorBridge.StartExport(projectJson);
        AndroidStartExportResponse start = SafeFromJson<AndroidStartExportResponse>(startJson);
        if (start == null || !start.ok || !start.accepted || string.IsNullOrEmpty(start.jobId))
        {
            Debug.LogError("ManualAndroidVideoComposerUI: startExport failed => " + startJson);
            SetStatus("Failed: could not start export.\nOutput path:\n" + outputPath, true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        while (true)
        {
            string stateJson = AndroidVideoEditorBridge.GetJobState(start.jobId);
            if (!AndroidJobPolling.TryParseJobState(stateJson, out AndroidJobStateResponse state))
            {
                Debug.LogWarning("ManualAndroidVideoComposerUI: Failed to parse job state => " + stateJson);
                SetStatus("Processing: waiting for export status...", true);
                yield return new WaitForSeconds(pollIntervalSeconds);
                continue;
            }

            Debug.Log($"ManualAndroidVideoComposerUI: job={state.state.jobId} status={state.state.status} progress={state.state.progressPercent}%");
            SetStatus($"Processing: {state.state.status} ({state.state.progressPercent}%)", true);

            string status = state.state.status == null ? string.Empty : state.state.status.ToLowerInvariant();
            if (status == "succeeded")
            {
                lastOutputPath = outputPath;
                Debug.Log("ManualAndroidVideoComposerUI: Export succeeded => " + outputPath);
                if (!string.IsNullOrEmpty(state.state.outputUri))
                {
                    Debug.Log("ManualAndroidVideoComposerUI: outputUri (state) => " + state.state.outputUri);
                }

                SetStatus($"Finished: export succeeded\nOutput path:\n{outputPath}", true);
                break;
            }

            if (AndroidJobPolling.IsTerminalStatus(status))
            {
                Debug.LogError("ManualAndroidVideoComposerUI: Export ended with status => " + stateJson);
                if (!string.IsNullOrEmpty(state.state.outputUri))
                {
                    Debug.Log("ManualAndroidVideoComposerUI: outputUri (state) => " + state.state.outputUri);
                }

                SetStatus($"Failed: export ended with status '{status}'.\nOutput path:\n{outputPath}", true);
                break;
            }

            yield return new WaitForSeconds(pollIntervalSeconds);
        }

        AndroidVideoEditorBridge.ReleaseJob(start.jobId);
        busy = false;
        SetButtonsInteractable(true);

        if (hidePopupWhenIdle && lastOutputPath == null)
        {
            SetPopupVisible(false);
        }
    }

    public void SetPickedVideosForTesting(string firstUri, string firstName, string secondUri, string secondName)
    {
        firstVideo = new AndroidGalleryVideoEntry
        {
            uri = firstUri,
            displayName = string.IsNullOrEmpty(firstName) ? "video_a.mp4" : firstName,
            durationMs = 0,
            sizeBytes = 0,
            mimeType = "video/mp4",
            dateAddedSec = 0,
        };

        secondVideo = new AndroidGalleryVideoEntry
        {
            uri = secondUri,
            displayName = string.IsNullOrEmpty(secondName) ? "video_b.mp4" : secondName,
            durationMs = 0,
            sizeBytes = 0,
            mimeType = "video/mp4",
            dateAddedSec = 0,
        };

        ApplyVideoToMatchingPlane(firstVideo, 0);
        ApplyVideoToMatchingPlane(secondVideo, 1);
        SetStatus($"Finished: test videos set\n1) {firstVideo.displayName}\n2) {secondVideo.displayName}", true);
    }

    private IEnumerator PickTwoVideosWithNativeGallery(Action<AndroidGalleryVideoEntry[]> onCompleted)
    {
        onCompleted ??= delegate { };

        bool permissionDone = false;
        bool permissionGranted = false;
        RequestNativeGalleryReadPermission((granted) =>
        {
            permissionGranted = granted;
            permissionDone = true;
        });
        while (!permissionDone)
        {
            yield return null;
        }

        if (!permissionGranted)
        {
            SetStatus("Failed: gallery permission denied.", true);
            onCompleted(null);
            yield break;
        }

        SetStatus("Processing: opening gallery picker...", true);
        string[] pickedPaths = null;

        if (CanSelectMultipleVideosViaNativeGallery())
        {
            bool pickerDone = false;
            PickVideosViaNativeGallery(true, "Select 2 videos", (paths) =>
            {
                pickedPaths = paths;
                pickerDone = true;
            });

            while (!pickerDone)
            {
                yield return null;
            }
        }

        if (pickedPaths == null || pickedPaths.Length < 2)
        {
            string firstPath = (pickedPaths != null && pickedPaths.Length > 0) ? pickedPaths[0] : null;
            string secondPath = null;

            if (string.IsNullOrEmpty(firstPath))
            {
                yield return PickSingleVideoWithNativeGallery("Select first video", (path) => firstPath = path);
            }
            if (string.IsNullOrEmpty(firstPath))
            {
                onCompleted(null);
                yield break;
            }

            yield return PickSingleVideoWithNativeGallery("Select second video", (path) => secondPath = path);
            if (string.IsNullOrEmpty(secondPath))
            {
                onCompleted(null);
                yield break;
            }

            pickedPaths = new[] { firstPath, secondPath };
        }

        List<AndroidGalleryVideoEntry> entries = new List<AndroidGalleryVideoEntry>(2);
        for (int i = 0; i < pickedPaths.Length && entries.Count < 2; i++)
        {
            AndroidGalleryVideoEntry entry = BuildEntryFromLocalPath(pickedPaths[i]);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        onCompleted(entries.Count >= 2 ? entries.ToArray() : null);
    }

    private IEnumerator PickSingleVideoWithNativeGallery(string title, Action<string> onCompleted)
    {
        onCompleted ??= delegate { };

        bool done = false;
        string pickedPath = null;

        PickVideosViaNativeGallery(false, title, (paths) =>
        {
            pickedPath = (paths != null && paths.Length > 0) ? paths[0] : null;
            done = true;
        });

        while (!done)
        {
            yield return null;
        }

        onCompleted(pickedPath);
    }

    private void RequestNativeGalleryReadPermission(Action<bool> onCompleted)
    {
        onCompleted ??= delegate { };
        RequestReadPermissionViaNativeGallery(onCompleted);
    }

    private AndroidGalleryVideoEntry BuildEntryFromLocalPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        long durationMs = 0;
        try
        {
            durationMs = GetVideoDurationMsViaNativeGallery(path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: could not read video properties for " + path + ". " + ex.Message);
        }

        return new AndroidGalleryVideoEntry
        {
            uri = path.Replace("\\", "/"),
            displayName = Path.GetFileName(path),
            durationMs = durationMs,
            sizeBytes = 0,
            mimeType = "video/*",
            dateAddedSec = 0,
        };
    }

    private bool CanSelectMultipleVideosViaNativeGallery()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            return NativeGalleryClass.CallStatic<bool>("CanSelectMultipleMedia");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: CanSelectMultipleMedia failed. " + ex.Message);
            return false;
        }
#else
        return false;
#endif
    }

    private void RequestReadPermissionViaNativeGallery(Action<bool> onCompleted)
    {
        onCompleted ??= delegate { };
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            int current = NativeGalleryClass.CallStatic<int>("CheckPermission", AndroidActivity, true, NativeGalleryMediaTypeVideo);
            if (current == 1)
            {
                onCompleted(true);
                return;
            }

            NativeGalleryClass.CallStatic(
                "RequestPermission",
                AndroidActivity,
                new NativeGalleryPermissionReceiver(result => onCompleted(result == 1)),
                true,
                NativeGalleryMediaTypeVideo);
        }
        catch (Exception ex)
        {
            Debug.LogError("ManualAndroidVideoComposerUI: RequestPermission failed. " + ex);
            onCompleted(false);
        }
#else
        onCompleted(false);
#endif
    }

    private void PickVideosViaNativeGallery(bool multiple, string title, Action<string[]> onCompleted)
    {
        onCompleted ??= delegate { };
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            Directory.CreateDirectory(Application.temporaryCachePath);
            string pickedMediaPath = Path.Combine(Application.temporaryCachePath, "pickedMedia");

            NativeGalleryClass.CallStatic(
                "PickMedia",
                AndroidActivity,
                new NativeGalleryMediaReceiver(
                    singlePath => onCompleted(string.IsNullOrEmpty(singlePath) ? null : new[] { singlePath }),
                    multiplePaths => onCompleted(multiplePaths)),
                NativeGalleryMediaTypeVideo,
                multiple,
                pickedMediaPath,
                "video/*",
                title ?? string.Empty);
        }
        catch (Exception ex)
        {
            Debug.LogError("ManualAndroidVideoComposerUI: PickMedia failed. " + ex);
            onCompleted(null);
        }
#else
        onCompleted(null);
#endif
    }

    private long GetVideoDurationMsViaNativeGallery(string path)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            string value = NativeGalleryClass.CallStatic<string>("GetVideoProperties", AndroidActivity, path);
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            string[] parts = value.Split('>');
            if (parts.Length >= 3 && long.TryParse(parts[2].Trim(), out long duration))
            {
                return duration;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: GetVideoProperties failed. " + ex.Message);
        }

        return 0;
#else
        return 0;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private const string NativeGalleryClassName = "com.yasirkula.unity.NativeGallery";
    private const string UnityPlayerClassName = "com.unity3d.player.UnityPlayer";
    private const int NativeGalleryMediaTypeVideo = 2;

    private AndroidJavaClass nativeGalleryClass;
    private AndroidJavaObject androidActivity;

    private AndroidJavaClass NativeGalleryClass
    {
        get
        {
            if (nativeGalleryClass == null)
            {
                nativeGalleryClass = new AndroidJavaClass(NativeGalleryClassName);
            }

            return nativeGalleryClass;
        }
    }

    private AndroidJavaObject AndroidActivity
    {
        get
        {
            if (androidActivity == null)
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass(UnityPlayerClassName))
                {
                    androidActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }
            }

            return androidActivity;
        }
    }

    private sealed class NativeGalleryPermissionReceiver : AndroidJavaProxy
    {
        private readonly Action<int> callback;

        public NativeGalleryPermissionReceiver(Action<int> callback)
            : base("com.yasirkula.unity.NativeGalleryPermissionReceiver")
        {
            this.callback = callback;
        }

        [UnityEngine.Scripting.Preserve]
        public void OnPermissionResult(int result)
        {
            callback?.Invoke(result);
        }
    }

    private sealed class NativeGalleryMediaReceiver : AndroidJavaProxy
    {
        private readonly Action<string> singleCallback;
        private readonly Action<string[]> multipleCallback;

        public NativeGalleryMediaReceiver(Action<string> singleCallback, Action<string[]> multipleCallback)
            : base("com.yasirkula.unity.NativeGalleryMediaReceiver")
        {
            this.singleCallback = singleCallback;
            this.multipleCallback = multipleCallback;
        }

        [UnityEngine.Scripting.Preserve]
        public void OnMediaReceived(string path)
        {
            singleCallback?.Invoke(string.IsNullOrEmpty(path) ? null : path);
        }

        [UnityEngine.Scripting.Preserve]
        public void OnMultipleMediaReceived(string paths)
        {
            if (string.IsNullOrEmpty(paths))
            {
                multipleCallback?.Invoke(null);
                return;
            }

            string[] split = paths.Split('>');
            int validCount = 0;
            for (int i = 0; i < split.Length; i++)
            {
                if (!string.IsNullOrEmpty(split[i]))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                multipleCallback?.Invoke(null);
                return;
            }

            if (validCount == split.Length)
            {
                multipleCallback?.Invoke(split);
                return;
            }

            string[] compact = new string[validCount];
            for (int i = 0, j = 0; i < split.Length; i++)
            {
                if (!string.IsNullOrEmpty(split[i]))
                {
                    compact[j++] = split[i];
                }
            }

            multipleCallback?.Invoke(compact);
        }
    }
#endif

    private void ApplyVideoToMatchingPlane(AndroidGalleryVideoEntry entry, int slot)
    {
        if (entry == null)
        {
            return;
        }

        if (setAllUnmatchedPlanesInactive)
        {
            SetAllPlanesActive(false);
        }

        string projectionKey = MapProjectionToKey(entry.displayName);
        PlaneBinding binding = FindBindingByKey(projectionKey) ?? FindBindingByKey("Flat3D");
        if (binding == null)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: No matching plane binding found for key " + projectionKey);
            return;
        }

        if (binding.planeObject != null)
        {
            binding.planeObject.SetActive(true);
        }

        if (binding.videoPlayer == null)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: Binding has no VideoPlayer for key " + projectionKey);
            return;
        }

        binding.videoPlayer.Stop();
        binding.videoPlayer.source = VideoSource.Url;
        binding.videoPlayer.url = entry.uri;
        binding.videoPlayer.playOnAwake = false;
        binding.videoPlayer.isLooping = true;
        binding.videoPlayer.Prepare();
        StartCoroutine(PlayWhenPrepared(binding.videoPlayer));

        Debug.Log($"ManualAndroidVideoComposerUI: Slot {slot + 1} -> {projectionKey} -> {entry.displayName}");
    }

    private IEnumerator PlayWhenPrepared(VideoPlayer player)
    {
        if (player == null)
        {
            yield break;
        }

        float timeout = 10f;
        float elapsed = 0f;
        while (!player.isPrepared && elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (player.isPrepared)
        {
            player.Play();
        }
    }

    private string MapProjectionToKey(string fileName)
    {
        FilenameVideoDetectionResult detection = FilenameVideoTypeDetector.Detect(fileName);
        if (detection == null)
        {
            return "Flat3D";
        }

        if (detection.StereoLayout == DetectedStereoLayout.Unknown)
        {
            // When stereo type can't be determined from filename, force Flat3D (SBS L/R default).
            return "Flat3D";
        }

        if (detection.IsGuess &&
            detection.Projection == DetectedProjection.Flat &&
            detection.StereoLayout == DetectedStereoLayout.Mono)
        {
            // Name didn't contain reliable type tags: default to Flat3D.
            return "Flat3D";
        }

        switch (detection.Projection)
        {
            case DetectedProjection.VR180:
            case DetectedProjection.Fisheye180:
                return "VR180";
            case DetectedProjection.Fisheye190:
                return "VR190";
            case DetectedProjection.Fisheye200:
                return "VR200";
            case DetectedProjection.Fisheye220:
                return "VR220";
            case DetectedProjection.VR360:
            case DetectedProjection.EAC360:
                return "VR360";
            default:
                return "Flat";
        }
    }

    private PlaneBinding FindBindingByKey(string key)
    {
        if (planeBindings == null)
        {
            return null;
        }

        for (int i = 0; i < planeBindings.Length; i++)
        {
            PlaneBinding binding = planeBindings[i];
            if (binding != null && string.Equals(binding.projectionKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return binding;
            }
        }

        return null;
    }

    private void SetAllPlanesActive(bool active)
    {
        if (planeBindings == null)
        {
            return;
        }

        for (int i = 0; i < planeBindings.Length; i++)
        {
            PlaneBinding binding = planeBindings[i];
            if (binding != null && binding.planeObject != null)
            {
                binding.planeObject.SetActive(active);
            }
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (pickTwoVideosButton != null)
        {
            pickTwoVideosButton.interactable = interactable;
        }

        if (saveButton != null)
        {
            saveButton.interactable = interactable;
        }
    }

    private void SetStatus(string message, bool showPopup)
    {
        if (showPopup)
        {
            SetPopupVisible(true);
        }

        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void SetPopupVisible(bool visible)
    {
        if (statusPopupObject != null)
        {
            statusPopupObject.SetActive(visible);
        }
    }

    private string BuildProjectJson(AndroidGalleryVideoEntry[] inputs, string outputUri)
    {
        StringBuilder sb = new StringBuilder(2048);
        sb.Append("{\"version\":1,\"assets\":[");

        for (int i = 0; i < inputs.Length; i++)
        {
            AndroidGalleryVideoEntry input = inputs[i];
            string assetId = "v" + (i + 1);
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append("{\"id\":\"");
            sb.Append(EscapeJson(assetId));
            sb.Append("\",\"type\":\"video\",\"uri\":\"");
            sb.Append(EscapeJson(input.uri ?? string.Empty));
            sb.Append("\"}");
        }

        sb.Append("],\"videoTrack\":[");

        for (int i = 0; i < inputs.Length; i++)
        {
            AndroidGalleryVideoEntry input = inputs[i];
            string assetId = "v" + (i + 1);
            long endMs = input.durationMs > 0 ? input.durationMs : 6000;

            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append("{\"assetId\":\"");
            sb.Append(EscapeJson(assetId));
            sb.Append("\",\"trimStartMs\":0,\"trimEndMs\":");
            sb.Append(endMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"frameRate\":");
            sb.Append(Mathf.Max(1, outputFps).ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"removeAudio\":false,\"volume\":1.0,\"effects\":{\"brightness\":0.0,\"contrast\":1.0,\"saturation\":1.0,\"overlayIds\":[]}");
            sb.Append(",\"transitionIn\":{\"type\":\"fade_from_black\",\"durationMs\":200}");
            sb.Append(",\"transitionOut\":{\"type\":\"dip_to_black\",\"durationMs\":200}");
            sb.Append("}");
        }

        sb.Append("],\"audioTracks\":[],\"overlays\":[],\"export\":{");
        sb.Append("\"outputUri\":\"");
        sb.Append(EscapeJson(outputUri));
        sb.Append("\",\"videoMimeType\":\"video/avc\",\"audioMimeType\":\"audio/mp4a-latm\",\"width\":");
        sb.Append(Mathf.Max(2, outputWidth).ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"height\":");
        sb.Append(Mathf.Max(2, outputHeight).ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"fps\":");
        sb.Append(Mathf.Max(1, outputFps).ToString(CultureInfo.InvariantCulture));
        sb.Append("}}");

        return sb.ToString();
    }

    private string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 32)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    private string BuildOutputFileName(string firstName, string secondName)
    {
        string cleanA = SanitizeBaseName(Path.GetFileNameWithoutExtension(firstName));
        string cleanB = SanitizeBaseName(Path.GetFileNameWithoutExtension(secondName));
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"SpatialEditor_{cleanA}_{cleanB}_{timestamp}.mp4";
    }

    private string SanitizeBaseName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "video";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] buffer = value.Trim().ToCharArray();
        for (int i = 0; i < buffer.Length; i++)
        {
            char c = buffer[i];
            if (Array.IndexOf(invalid, c) >= 0 || char.IsWhiteSpace(c))
            {
                buffer[i] = '_';
            }
        }

        string cleaned = new string(buffer);
        while (cleaned.Contains("__"))
        {
            cleaned = cleaned.Replace("__", "_");
        }

        return cleaned.Length > 32 ? cleaned.Substring(0, 32) : cleaned;
    }

    private T SafeFromJson<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch
        {
            return null;
        }
    }

}
