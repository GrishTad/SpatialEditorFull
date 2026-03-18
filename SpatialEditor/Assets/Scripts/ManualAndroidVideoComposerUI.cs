using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        [Tooltip("Optional renderer with stereo shader (_StereoMode). If empty, planeObject renderer is used.")]
        public Renderer targetRenderer;
    }

    [Header("UI")]
    [SerializeField] private Button pickTwoVideosButton;
    [SerializeField] private Button addVideoButton;
    [SerializeField] private Button addAudioButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private GameObject statusPopupObject;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private bool hidePopupWhenIdle;

    [Header("Plane Bindings")]
    [Tooltip("Add one row per plane you want to control.")]
    [SerializeField] private PlaneBinding[] planeBindings;

    [Header("Picker")]
    [Tooltip("Pick videos one-by-one to avoid Android Photo Picker synthetic names from multi-select.")]
    [SerializeField] private bool forceSinglePickerEvenIfMultiSupported = true;
    [Tooltip("0 = unlimited")]
    [SerializeField] private int maxSelectableVideos = 0;
    [Tooltip("If filename type is unknown, force SBS Left-Right stereo for Flat3D.")]
    [SerializeField] private bool forceSbsStereoForFlat3DFallback = true;

    [Header("Export")]
    [SerializeField] private float pollIntervalSeconds = 0.5f;
    [SerializeField] private int outputWidth = 1920;
    [SerializeField] private int outputHeight = 1080;
    [SerializeField] private int outputFps = 30;
    [SerializeField] private bool setAllUnmatchedPlanesInactive = true;

    private readonly SpatialProject spatialProject = new SpatialProject();
    private bool busy;
    private bool editorBridgeInitialized;
    private bool nativeGalleryPickerConfigured;
    private string lastOutputPath;
    private readonly List<AndroidGalleryVideoEntry> galleryNameCacheEntries = new List<AndroidGalleryVideoEntry>();
    private readonly Dictionary<long, AndroidGalleryVideoEntry> galleryNameCacheById = new Dictionary<long, AndroidGalleryVideoEntry>();
    private static readonly int StereoModeShaderId = Shader.PropertyToID("_StereoMode");
    private const int NativeGalleryMediaTypeVideo = 2;
    private const int NativeGalleryMediaTypeAudio = 4;

    public string LastOutputPath => lastOutputPath;

    private void Awake()
    {
        if (pickTwoVideosButton != null)
        {
            pickTwoVideosButton.onClick.AddListener(OnAddVideoButtonClicked);
        }

        if (addVideoButton != null && addVideoButton != pickTwoVideosButton)
        {
            addVideoButton.onClick.AddListener(OnAddVideoButtonClicked);
        }

        if (addAudioButton != null)
        {
            addAudioButton.onClick.AddListener(OnAddAudioButtonClicked);
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
            pickTwoVideosButton.onClick.RemoveListener(OnAddVideoButtonClicked);
        }

        if (addVideoButton != null && addVideoButton != pickTwoVideosButton)
        {
            addVideoButton.onClick.RemoveListener(OnAddVideoButtonClicked);
        }

        if (addAudioButton != null)
        {
            addAudioButton.onClick.RemoveListener(OnAddAudioButtonClicked);
        }

        if (saveButton != null)
        {
            saveButton.onClick.RemoveListener(OnSaveButtonClicked);
        }
    }

    public void OnPickTwoVideosButtonClicked()
    {
        OnAddVideoButtonClicked();
    }

    public void OnAddVideoButtonClicked()
    {
        if (!busy)
        {
            StartCoroutine(AddVideoFlow());
        }
    }

    public void OnSaveButtonClicked()
    {
        if (!busy)
        {
            StartCoroutine(ExportFlow());
        }
    }

    public void OnAddAudioButtonClicked()
    {
        if (!busy)
        {
            StartCoroutine(AddAudioFlow());
        }
    }

    public SpatialProject GetSpatialProject()
    {
        return spatialProject;
    }

    public void ClearProjectMedia()
    {
        spatialProject.videos.Clear();
        spatialProject.audios.Clear();
        SetStatus("Finished: project media cleared.", true);
    }

    public bool MoveVideo(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= spatialProject.videos.Count)
        {
            return false;
        }

        if (toIndex < 0 || toIndex >= spatialProject.videos.Count)
        {
            return false;
        }

        if (fromIndex == toIndex)
        {
            return true;
        }

        SpatialVideoMedia item = spatialProject.videos[fromIndex];
        spatialProject.videos.RemoveAt(fromIndex);
        spatialProject.videos.Insert(toIndex, item);

        if (setAllUnmatchedPlanesInactive)
        {
            SetAllPlanesActive(false);
        }
        for (int i = 0; i < spatialProject.videos.Count; i++)
        {
            ApplyVideoToMatchingPlane(spatialProject.videos[i], i);
        }

        SetStatus(BuildSelectedVideosStatus(), true);
        return true;
    }

    public bool RemoveVideoAt(int index)
    {
        if (index < 0 || index >= spatialProject.videos.Count)
        {
            return false;
        }

        spatialProject.videos.RemoveAt(index);

        if (setAllUnmatchedPlanesInactive)
        {
            SetAllPlanesActive(false);
        }
        for (int i = 0; i < spatialProject.videos.Count; i++)
        {
            ApplyVideoToMatchingPlane(spatialProject.videos[i], i);
        }

        SetStatus(BuildSelectedVideosStatus(), true);
        return true;
    }

    public bool SetVideoTrim(int videoIndex, long trimStartMs, long trimEndMs)
    {
        if (videoIndex < 0 || videoIndex >= spatialProject.videos.Count)
        {
            return false;
        }

        SpatialVideoMedia video = spatialProject.videos[videoIndex];
        if (video == null || video.edits == null)
        {
            return false;
        }

        long safeStart = Math.Max(0, trimStartMs);
        long safeEnd = Math.Max(safeStart + 1, trimEndMs);

        video.edits.trimStartMs = safeStart;
        video.edits.trimEndMs = safeEnd;
        return true;
    }

    public bool SetVideoColorAdjustments(int videoIndex, double brightness, double contrast, double saturation)
    {
        if (videoIndex < 0 || videoIndex >= spatialProject.videos.Count)
        {
            return false;
        }

        SpatialVideoMedia video = spatialProject.videos[videoIndex];
        if (video == null || video.edits == null)
        {
            return false;
        }

        video.edits.brightness = brightness;
        video.edits.contrast = contrast;
        video.edits.saturation = saturation;
        return true;
    }

    public bool SetVideoLut(int videoIndex, string lutUri)
    {
        if (videoIndex < 0 || videoIndex >= spatialProject.videos.Count)
        {
            return false;
        }

        SpatialVideoMedia video = spatialProject.videos[videoIndex];
        if (video == null || video.edits == null)
        {
            return false;
        }

        video.edits.lut = string.IsNullOrWhiteSpace(lutUri) ? null : lutUri;
        return true;
    }

    public bool SetVideoAudioMute(int videoIndex, bool muted)
    {
        if (videoIndex < 0 || videoIndex >= spatialProject.videos.Count)
        {
            return false;
        }

        SpatialVideoMedia video = spatialProject.videos[videoIndex];
        if (video == null || video.edits == null)
        {
            return false;
        }

        video.edits.isAudioMuted = muted;
        return true;
    }

    public bool AddAudioMedia(string uri, string displayName, long durationMs, long sizeBytes = 0, string mimeType = "audio/*")
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        int index = spatialProject.audios.Count + 1;
        spatialProject.audios.Add(new SpatialAudioMedia
        {
            id = GenerateNextAudioId(),
            sourceUri = uri,
            displayName = string.IsNullOrWhiteSpace(displayName) ? ("audio_" + index + ".mp3") : displayName,
            durationMs = Math.Max(0, durationMs),
            sizeBytes = Math.Max(0, sizeBytes),
            mimeType = string.IsNullOrWhiteSpace(mimeType) ? "audio/*" : mimeType,
            edits = new SpatialAudioEditSettings
            {
                trimStartMs = 0,
                trimEndMs = durationMs > 0 ? durationMs : 1000,
                loop = false,
                volume = 1.0,
                sequenceStartMs = 0
            }
        });

        return true;
    }

    public void AddAudioMediaForTesting(string uri, string displayName, long durationMs)
    {
        AddAudioMedia(uri, displayName, durationMs, 0, "audio/*");
    }

    public bool MoveAudio(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= spatialProject.audios.Count)
        {
            return false;
        }

        if (toIndex < 0 || toIndex >= spatialProject.audios.Count)
        {
            return false;
        }

        if (fromIndex == toIndex)
        {
            return true;
        }

        SpatialAudioMedia item = spatialProject.audios[fromIndex];
        spatialProject.audios.RemoveAt(fromIndex);
        spatialProject.audios.Insert(toIndex, item);
        return true;
    }

    public bool RemoveAudioAt(int index)
    {
        if (index < 0 || index >= spatialProject.audios.Count)
        {
            return false;
        }

        spatialProject.audios.RemoveAt(index);
        return true;
    }

    public bool SetAudioTrim(int audioIndex, long trimStartMs, long trimEndMs)
    {
        if (audioIndex < 0 || audioIndex >= spatialProject.audios.Count)
        {
            return false;
        }

        SpatialAudioMedia audio = spatialProject.audios[audioIndex];
        if (audio == null || audio.edits == null)
        {
            return false;
        }

        long safeStart = Math.Max(0, trimStartMs);
        long safeEnd = Math.Max(safeStart + 1, trimEndMs);
        audio.edits.trimStartMs = safeStart;
        audio.edits.trimEndMs = safeEnd;
        return true;
    }

    public bool SetAudioVolume(int audioIndex, double volume)
    {
        if (audioIndex < 0 || audioIndex >= spatialProject.audios.Count)
        {
            return false;
        }

        SpatialAudioMedia audio = spatialProject.audios[audioIndex];
        if (audio == null || audio.edits == null)
        {
            return false;
        }

        audio.edits.volume = volume;
        return true;
    }

    public bool SetAudioSequenceStart(int audioIndex, long sequenceStartMs)
    {
        if (audioIndex < 0 || audioIndex >= spatialProject.audios.Count)
        {
            return false;
        }

        SpatialAudioMedia audio = spatialProject.audios[audioIndex];
        if (audio == null || audio.edits == null)
        {
            return false;
        }

        audio.edits.sequenceStartMs = Math.Max(0, sequenceStartMs);
        return true;
    }

    private string GenerateNextAudioId()
    {
        int suffix = spatialProject.audios.Count + 1;
        string candidate = "a" + suffix.ToString(CultureInfo.InvariantCulture);
        HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < spatialProject.audios.Count; i++)
        {
            SpatialAudioMedia audio = spatialProject.audios[i];
            if (audio != null && !string.IsNullOrWhiteSpace(audio.id))
            {
                used.Add(audio.id);
            }
        }

        while (used.Contains(candidate))
        {
            suffix++;
            candidate = "a" + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    private IEnumerator AddVideoFlow()
    {
        busy = true;
        SetButtonsInteractable(false);
        SetStatus("Processing: opening add video...", true);

        if (!AndroidVideoEditorBridge.IsAndroidRuntime)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: Android runtime only.");
            SetStatus("Failed: Android runtime only.", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        if (maxSelectableVideos > 0 && spatialProject.videos.Count >= maxSelectableVideos)
        {
            SetStatus($"Failed: max videos reached ({maxSelectableVideos}).", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        SetStatus("Processing: requesting gallery permission...", true);
        AndroidGalleryVideoEntry picked = null;
        yield return PickSingleVideoEntryWithNativeGallery((result) => picked = result);

        if (picked == null)
        {
            Debug.LogError("ManualAndroidVideoComposerUI: Could not add video.");
            SetStatus("Failed: could not add video.", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        SpatialVideoMedia media = CreateVideoMediaFromEntry(picked);
        spatialProject.videos.Add(media);
        if (setAllUnmatchedPlanesInactive)
        {
            SetAllPlanesActive(false);
        }
        for (int i = 0; i < spatialProject.videos.Count; i++)
        {
            ApplyVideoToMatchingPlane(spatialProject.videos[i], i);
        }

        Debug.Log($"ManualAndroidVideoComposerUI: Added video {spatialProject.videos.Count} => {media.displayName}");
        SetStatus(BuildSelectedVideosStatus(), true);

        busy = false;
        SetButtonsInteractable(true);
    }

    private IEnumerator AddAudioFlow()
    {
        busy = true;
        SetButtonsInteractable(false);
        SetStatus("Processing: opening add audio...", true);

        if (!AndroidVideoEditorBridge.IsAndroidRuntime)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: Android runtime only.");
            SetStatus("Failed: Android runtime only.", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        SetStatus("Processing: requesting gallery permission...", true);
        string pickedPath = null;
        yield return PickSingleAudioWithNativeGallery("Select audio", (path) => pickedPath = path);

        if (string.IsNullOrEmpty(pickedPath))
        {
            Debug.LogError("ManualAndroidVideoComposerUI: Could not add audio.");
            SetStatus("Failed: could not add audio.", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        SpatialAudioMedia media = CreateAudioMediaFromLocalPath(pickedPath);
        if (media == null)
        {
            Debug.LogError("ManualAndroidVideoComposerUI: Could not parse selected audio.");
            SetStatus("Failed: could not read selected audio.", true);
            busy = false;
            SetButtonsInteractable(true);
            yield break;
        }

        spatialProject.audios.Add(media);
        Debug.Log($"ManualAndroidVideoComposerUI: Added audio {spatialProject.audios.Count} => {media.displayName}");
        SetStatus(BuildSelectedVideosStatus(), true);

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

        if (spatialProject.videos == null || spatialProject.videos.Count == 0)
        {
            Debug.LogError("ManualAndroidVideoComposerUI: Add at least 1 video before exporting.");
            SetStatus("Failed: add at least 1 video before exporting.", true);
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

        string outputFileName = BuildOutputFileName(spatialProject.videos);
        string outputPath = Path.Combine(Application.persistentDataPath, outputFileName);
        string outputUri = "file://" + outputPath.Replace("\\", "/");
        Debug.Log("ManualAndroidVideoComposerUI: outputPath => " + outputPath);
        Debug.Log("ManualAndroidVideoComposerUI: outputUri => " + outputUri);
        SetStatus("Processing: output path prepared...\n" + outputPath, true);

        spatialProject.export.outputUri = outputUri;
        spatialProject.export.width = outputWidth;
        spatialProject.export.height = outputHeight;
        spatialProject.export.fps = outputFps;
        WarnAboutUnsupportedAudioSequenceOffsets();
        string projectJson = SpatialProjectMapper.ToProjectJson(spatialProject);
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

    private void WarnAboutUnsupportedAudioSequenceOffsets()
    {
        for (int i = 0; i < spatialProject.audios.Count; i++)
        {
            SpatialAudioMedia audio = spatialProject.audios[i];
            if (audio == null || audio.edits == null)
            {
                continue;
            }

            if (audio.edits.sequenceStartMs > 0)
            {
                Debug.LogWarning(
                    "ManualAndroidVideoComposerUI: audio sequenceStartMs is currently not mapped to Android export schema. " +
                    "Track index=" + i + ", requestedStartMs=" + audio.edits.sequenceStartMs);
            }
        }
    }

    public void SetPickedVideosForTesting(string firstUri, string firstName, string secondUri, string secondName)
    {
        spatialProject.videos.Clear();

        AndroidGalleryVideoEntry first = new AndroidGalleryVideoEntry
        {
            uri = firstUri,
            displayName = string.IsNullOrEmpty(firstName) ? "video_a.mp4" : firstName,
            durationMs = 0,
            sizeBytes = 0,
            mimeType = "video/mp4",
            dateAddedSec = 0,
        };

        AndroidGalleryVideoEntry second = new AndroidGalleryVideoEntry
        {
            uri = secondUri,
            displayName = string.IsNullOrEmpty(secondName) ? "video_b.mp4" : secondName,
            durationMs = 0,
            sizeBytes = 0,
            mimeType = "video/mp4",
            dateAddedSec = 0,
        };

        SpatialVideoMedia firstMedia = CreateVideoMediaFromEntry(first);
        SpatialVideoMedia secondMedia = CreateVideoMediaFromEntry(second);
        spatialProject.videos.Add(firstMedia);
        spatialProject.videos.Add(secondMedia);

        if (setAllUnmatchedPlanesInactive)
        {
            SetAllPlanesActive(false);
        }
        ApplyVideoToMatchingPlane(firstMedia, 0);
        ApplyVideoToMatchingPlane(secondMedia, 1);
        SetStatus(BuildSelectedVideosStatus(), true);
    }

    private IEnumerator PickSingleVideoEntryWithNativeGallery(Action<AndroidGalleryVideoEntry> onCompleted)
    {
        onCompleted ??= delegate { };

        ConfigureNativeGalleryPickerForDisplayNames();

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
        Debug.Log("ManualAndroidVideoComposerUI: add pick start. forceSingle=" + forceSinglePickerEvenIfMultiSupported);

        string pickedPath = null;
        yield return PickSingleVideoWithNativeGallery("Select video", (path) => pickedPath = path);

        if (string.IsNullOrEmpty(pickedPath))
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: add pick returned empty path.");
            SetStatus("Failed: video was not selected.", true);
            onCompleted(null);
            yield break;
        }

        Debug.Log("ManualAndroidVideoComposerUI: add pick path => " + pickedPath);
        RefreshGalleryNameCache();
        AndroidGalleryVideoEntry entry = BuildEntryFromLocalPath(pickedPath);
        onCompleted(entry);
    }

    private IEnumerator PickTwoVideosWithNativeGallery(Action<AndroidGalleryVideoEntry[]> onCompleted)
    {
        onCompleted ??= delegate { };

        ConfigureNativeGalleryPickerForDisplayNames();

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
        Debug.Log("ManualAndroidVideoComposerUI: picker start. forceSingle=" + forceSinglePickerEvenIfMultiSupported);

        if (!forceSinglePickerEvenIfMultiSupported && CanSelectMultipleVideosViaNativeGallery())
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

            Debug.Log("ManualAndroidVideoComposerUI: multi pick result count=" + (pickedPaths == null ? 0 : pickedPaths.Length));
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
                Debug.LogWarning("ManualAndroidVideoComposerUI: first pick returned empty path.");
                SetStatus("Failed: first video was not selected.", true);
                onCompleted(null);
                yield break;
            }
            Debug.Log("ManualAndroidVideoComposerUI: first pick path => " + firstPath);

            yield return PickSingleVideoWithNativeGallery("Select second video", (path) => secondPath = path);
            if (string.IsNullOrEmpty(secondPath))
            {
                Debug.LogWarning("ManualAndroidVideoComposerUI: second pick returned empty path.");
                SetStatus("Failed: second video was not selected.", true);
                onCompleted(null);
                yield break;
            }
            Debug.Log("ManualAndroidVideoComposerUI: second pick path => " + secondPath);

            pickedPaths = new[] { firstPath, secondPath };
        }

        List<AndroidGalleryVideoEntry> entries = new List<AndroidGalleryVideoEntry>(2);
        RefreshGalleryNameCache();
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

    private IEnumerator PickSingleAudioWithNativeGallery(string title, Action<string> onCompleted)
    {
        onCompleted ??= delegate { };

        ConfigureNativeGalleryPickerForDisplayNames();

        bool permissionDone = false;
        bool permissionGranted = false;
        RequestNativeGalleryAudioReadPermission((granted) =>
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

        SetStatus("Processing: opening audio picker...", true);
        bool done = false;
        string pickedPath = null;

        PickAudiosViaNativeGallery(false, title, (paths) =>
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
        RequestReadPermissionViaNativeGallery(NativeGalleryMediaTypeVideo, onCompleted);
    }

    private void RequestNativeGalleryAudioReadPermission(Action<bool> onCompleted)
    {
        onCompleted ??= delegate { };
        RequestReadPermissionViaNativeGallery(NativeGalleryMediaTypeAudio, onCompleted);
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

        long sizeBytes = TryGetFileSizeBytes(path);
        string displayName = ResolveDisplayName(path, durationMs, sizeBytes);
        string fallbackName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(displayName) &&
            !string.Equals(displayName, fallbackName, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"ManualAndroidVideoComposerUI: Resolved display name '{displayName}' from metadata for '{fallbackName}'.");
        }

        return new AndroidGalleryVideoEntry
        {
            uri = path.Replace("\\", "/"),
            displayName = string.IsNullOrEmpty(displayName) ? Path.GetFileName(path) : displayName,
            durationMs = durationMs,
            sizeBytes = sizeBytes,
            mimeType = "video/*",
            dateAddedSec = 0,
        };
    }

    private SpatialVideoMedia CreateVideoMediaFromEntry(AndroidGalleryVideoEntry entry)
    {
        if (entry == null)
        {
            return null;
        }

        FilenameVideoDetectionResult detection = FilenameVideoTypeDetector.Detect(entry.displayName);
        long clipDurationMs = entry.durationMs > 0 ? entry.durationMs : 6000;

        return new SpatialVideoMedia
        {
            id = GenerateNextVideoId(),
            sourceUri = entry.uri,
            displayName = entry.displayName,
            durationMs = entry.durationMs,
            sizeBytes = entry.sizeBytes,
            mimeType = string.IsNullOrWhiteSpace(entry.mimeType) ? "video/*" : entry.mimeType,
            dateAddedSec = entry.dateAddedSec,
            projectionType = ToSpatialProjection(detection == null ? DetectedProjection.Unknown : detection.Projection),
            stereoLayout = ToSpatialStereo(detection == null ? DetectedStereoLayout.Unknown : detection.StereoLayout),
            edits = new SpatialVideoEditSettings
            {
                trimStartMs = 0,
                trimEndMs = clipDurationMs,
                isAudioMuted = false,
                audioVolume = 1.0,
                brightness = 0.0,
                contrast = 1.0,
                saturation = 1.0,
                lut = null,
                overlayIds = Array.Empty<string>(),
                transitionIn = new SpatialTransition { type = SpatialTransitionType.FadeFromBlack, durationMs = 200 },
                transitionOut = new SpatialTransition { type = SpatialTransitionType.DipToBlack, durationMs = 200 }
            }
        };
    }

    private SpatialAudioMedia CreateAudioMediaFromLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = path.Replace("\\", "/");
        long durationMs = GetAudioDurationMsViaMetadataRetriever(normalized);
        long sizeBytes = TryGetFileSizeBytes(path);
        string displayName = ResolveAudioDisplayName(normalized);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Path.GetFileName(normalized);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "audio_" + (spatialProject.audios.Count + 1).ToString(CultureInfo.InvariantCulture) + ".mp3";
        }

        return new SpatialAudioMedia
        {
            id = GenerateNextAudioId(),
            sourceUri = normalized,
            displayName = displayName,
            durationMs = Math.Max(0, durationMs),
            sizeBytes = Math.Max(0, sizeBytes),
            mimeType = "audio/*",
            edits = new SpatialAudioEditSettings
            {
                trimStartMs = 0,
                trimEndMs = durationMs > 0 ? durationMs : 1000,
                loop = false,
                volume = 1.0,
                sequenceStartMs = 0
            }
        };
    }

    private string ResolveAudioDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            string fromUri = QueryDisplayNameFromContentUri(path);
            if (!string.IsNullOrWhiteSpace(fromUri))
            {
                return fromUri.Trim();
            }
        }

        string fromTag = ResolveDisplayNameFromTagLib(path);
        if (!string.IsNullOrWhiteSpace(fromTag))
        {
            return fromTag.Trim();
        }

        return Path.GetFileName(path);
    }

    private string GenerateNextVideoId()
    {
        int suffix = spatialProject.videos.Count + 1;
        string candidate = "v" + suffix.ToString(CultureInfo.InvariantCulture);
        HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < spatialProject.videos.Count; i++)
        {
            SpatialVideoMedia video = spatialProject.videos[i];
            if (video != null && !string.IsNullOrWhiteSpace(video.id))
            {
                used.Add(video.id);
            }
        }

        while (used.Contains(candidate))
        {
            suffix++;
            candidate = "v" + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    private string ResolveDisplayName(string path, long durationMs, long sizeBytes)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string cacheName = ResolveDisplayNameFromGalleryCache(path, durationMs, sizeBytes);
        if (!string.IsNullOrEmpty(cacheName))
        {
            return cacheName;
        }

        string byPathDataName = ResolveDisplayNameByDataPath(path);
        if (!string.IsNullOrEmpty(byPathDataName))
        {
            return byPathDataName;
        }

        string mediaStoreName = ResolveDisplayNameFromMediaStore(path);
        if (!string.IsNullOrEmpty(mediaStoreName))
        {
            return mediaStoreName;
        }

        string tagTitleName = ResolveDisplayNameFromTagLib(path);
        if (!string.IsNullOrEmpty(tagTitleName))
        {
            return tagTitleName;
        }

        return Path.GetFileName(path);
    }

    private void RefreshGalleryNameCache()
    {
        galleryNameCacheEntries.Clear();
        galleryNameCacheById.Clear();

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (TryPopulateGalleryNameCacheWithContentResolver())
            {
                Debug.Log($"ManualAndroidVideoComposerUI: gallery cache loaded via resolver ({galleryNameCacheEntries.Count} videos).");
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: gallery resolver cache refresh failed. " + ex.Message);
        }

        try
        {
            if (TryPopulateGalleryNameCacheWithBridge())
            {
                Debug.Log($"ManualAndroidVideoComposerUI: gallery cache loaded via bridge ({galleryNameCacheEntries.Count} videos).");
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: gallery bridge cache refresh failed. " + ex.Message);
        }

        Debug.LogWarning("ManualAndroidVideoComposerUI: gallery cache lookup unavailable. Falling back to path/uri metadata.");
#endif
    }

    private bool TryPopulateGalleryNameCacheWithBridge()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidGalleryQueryConfig config = new AndroidGalleryQueryConfig
        {
            limit = 1000,
            includePending = true,
            bucketId = null
        };

        string json = AndroidVideoEditorBridge.QueryGalleryVideos(config);
        AndroidGalleryQueryResponse response = SafeFromJson<AndroidGalleryQueryResponse>(json);
        if (response == null || !response.ok || response.videos == null || response.videos.Length == 0)
        {
            Debug.Log("ManualAndroidVideoComposerUI: gallery bridge cache lookup skipped => " + json);
            return false;
        }

        for (int i = 0; i < response.videos.Length; i++)
        {
            AddGalleryCacheEntry(response.videos[i]);
        }

        return galleryNameCacheEntries.Count > 0;
#else
        return false;
#endif
    }

    private bool TryPopulateGalleryNameCacheWithContentResolver()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (AndroidJavaObject resolver = AndroidActivity.Call<AndroidJavaObject>("getContentResolver"))
        using (AndroidJavaClass mediaStoreVideoClass = new AndroidJavaClass("android.provider.MediaStore$Video$Media"))
        using (AndroidJavaObject externalVideoUri = mediaStoreVideoClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
        using (AndroidJavaClass contentUrisClass = new AndroidJavaClass("android.content.ContentUris"))
        {
            if (resolver == null || externalVideoUri == null || contentUrisClass == null)
            {
                return false;
            }

            string[] projection = { "_id", "_display_name", "duration", "_size", "mime_type", "date_added" };
            using (AndroidJavaObject cursor = resolver.Call<AndroidJavaObject>("query", externalVideoUri, projection, null, null, "date_added DESC"))
            {
                if (cursor == null)
                {
                    return false;
                }

                int idCol = cursor.Call<int>("getColumnIndex", "_id");
                int nameCol = cursor.Call<int>("getColumnIndex", "_display_name");
                int durationCol = cursor.Call<int>("getColumnIndex", "duration");
                int sizeCol = cursor.Call<int>("getColumnIndex", "_size");
                int mimeCol = cursor.Call<int>("getColumnIndex", "mime_type");
                int dateCol = cursor.Call<int>("getColumnIndex", "date_added");

                int maxRows = 2000;
                int count = 0;
                while (count < maxRows && cursor.Call<bool>("moveToNext"))
                {
                    if (idCol < 0)
                    {
                        break;
                    }

                    long id = cursor.Call<long>("getLong", idCol);
                    if (id <= 0)
                    {
                        continue;
                    }

                    using (AndroidJavaObject itemUri = contentUrisClass.CallStatic<AndroidJavaObject>("withAppendedId", externalVideoUri, id))
                    {
                        AndroidGalleryVideoEntry entry = new AndroidGalleryVideoEntry
                        {
                            uri = itemUri != null ? itemUri.Call<string>("toString") : ("content://media/external/video/media/" + id.ToString(CultureInfo.InvariantCulture)),
                            displayName = nameCol >= 0 ? cursor.Call<string>("getString", nameCol) : null,
                            durationMs = durationCol >= 0 ? cursor.Call<long>("getLong", durationCol) : 0,
                            sizeBytes = sizeCol >= 0 ? cursor.Call<long>("getLong", sizeCol) : 0,
                            mimeType = mimeCol >= 0 ? cursor.Call<string>("getString", mimeCol) : "video/*",
                            dateAddedSec = dateCol >= 0 ? cursor.Call<long>("getLong", dateCol) : 0,
                        };

                        if (string.IsNullOrWhiteSpace(entry.displayName))
                        {
                            entry.displayName = id.ToString(CultureInfo.InvariantCulture) + ".mp4";
                        }

                        AddGalleryCacheEntry(entry);
                        count++;
                    }
                }
            }

            return galleryNameCacheEntries.Count > 0;
        }
#else
        return false;
#endif
    }

    private void AddGalleryCacheEntry(AndroidGalleryVideoEntry video)
    {
        if (video == null)
        {
            return;
        }

        galleryNameCacheEntries.Add(video);
        long id = ExtractMediaStoreIdFromUri(video.uri);
        if (id > 0 && !galleryNameCacheById.ContainsKey(id))
        {
            galleryNameCacheById[id] = video;
        }
    }

    private string ResolveDisplayNameFromGalleryCache(string path, long durationMs, long sizeBytes)
    {
        if (galleryNameCacheEntries == null || galleryNameCacheEntries.Count == 0)
        {
            return null;
        }

        long pathId = ExtractMediaStoreIdFromPath(path);
        if (pathId > 0 &&
            galleryNameCacheById.TryGetValue(pathId, out AndroidGalleryVideoEntry idMatch) &&
            idMatch != null &&
            !string.IsNullOrWhiteSpace(idMatch.displayName))
        {
            return idMatch.displayName.Trim();
        }

        AndroidGalleryVideoEntry durationAndSizeMatch = null;
        AndroidGalleryVideoEntry sizeOnlyMatch = null;
        AndroidGalleryVideoEntry durationOnlyMatch = null;

        for (int i = 0; i < galleryNameCacheEntries.Count; i++)
        {
            AndroidGalleryVideoEntry candidate = galleryNameCacheEntries[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.displayName))
            {
                continue;
            }

            bool sizeMatch = sizeBytes > 0 && candidate.sizeBytes > 0 && candidate.sizeBytes == sizeBytes;
            bool durationMatch = durationMs > 0 && candidate.durationMs > 0 && Math.Abs(candidate.durationMs - durationMs) <= 750;

            if (sizeMatch && durationMatch)
            {
                durationAndSizeMatch = candidate;
                break;
            }

            if (sizeMatch && sizeOnlyMatch == null)
            {
                sizeOnlyMatch = candidate;
            }

            if (durationMatch && durationOnlyMatch == null)
            {
                durationOnlyMatch = candidate;
            }
        }

        AndroidGalleryVideoEntry best = durationAndSizeMatch ?? sizeOnlyMatch ?? durationOnlyMatch;
        if (best == null || string.IsNullOrWhiteSpace(best.displayName))
        {
            return null;
        }

        return best.displayName.Trim();
    }

    private string ResolveDisplayNameFromMediaStore(string path)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            long mediaId = ExtractMediaStoreIdFromPath(path);
            if (mediaId <= 0)
            {
                return null;
            }

            string pickerContentUri = BuildPhotoPickerContentUriFromSyntheticPath(path, mediaId);
            string[] candidateUris =
            {
                "content://media/external/video/media/" + mediaId.ToString(CultureInfo.InvariantCulture),
                "content://media/external_primary/video/media/" + mediaId.ToString(CultureInfo.InvariantCulture),
                "content://media/external/file/" + mediaId.ToString(CultureInfo.InvariantCulture),
                "content://media/external_primary/file/" + mediaId.ToString(CultureInfo.InvariantCulture),
                "content://media/internal/video/media/" + mediaId.ToString(CultureInfo.InvariantCulture),
                "content://media/internal/file/" + mediaId.ToString(CultureInfo.InvariantCulture),
                pickerContentUri,
            };

            string syntheticFallback = null;
            for (int i = 0; i < candidateUris.Length; i++)
            {
                string candidate = candidateUris[i];
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                string name = QueryDisplayNameFromContentUri(candidate);
                if (!string.IsNullOrEmpty(name))
                {
                    if (IsLikelySyntheticPickerName(name))
                    {
                        if (string.IsNullOrEmpty(syntheticFallback))
                        {
                            syntheticFallback = name;
                        }

                        continue;
                    }

                    Debug.Log($"ManualAndroidVideoComposerUI: Resolved _display_name '{name}' via uri '{candidate}'.");
                    return name;
                }
            }

            if (!string.IsNullOrEmpty(syntheticFallback))
            {
                return syntheticFallback;
            }

            if (!string.IsNullOrEmpty(pickerContentUri))
            {
                Debug.LogWarning("ManualAndroidVideoComposerUI: _display_name not found for picker uri: " + pickerContentUri);
            }

            return null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: MediaStore display name lookup failed. " + ex.Message);
            return null;
        }
#else
        return null;
#endif
    }

    private string ResolveDisplayNameByDataPath(string path)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string normalizedPath = path.Replace("\\", "/");
            using (AndroidJavaObject resolver = AndroidActivity.Call<AndroidJavaObject>("getContentResolver"))
            using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
            using (AndroidJavaObject filesExternal = uriClass.CallStatic<AndroidJavaObject>("parse", "content://media/external/file"))
            using (AndroidJavaObject filesExternalPrimary = uriClass.CallStatic<AndroidJavaObject>("parse", "content://media/external_primary/file"))
            {
                if (resolver == null)
                {
                    return null;
                }

                string[] projection = { "_display_name" };
                string selection = "_data=?";
                string[] selectionArgs = { normalizedPath };

                string fromExternal = QueryDisplayNameFromCursor(resolver, filesExternal, projection, selection, selectionArgs);
                if (!string.IsNullOrEmpty(fromExternal))
                {
                    return fromExternal;
                }

                return QueryDisplayNameFromCursor(resolver, filesExternalPrimary, projection, selection, selectionArgs);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: path-based display name lookup failed. " + ex.Message);
            return null;
        }
#else
        return null;
#endif
    }

    private string BuildPhotoPickerContentUriFromSyntheticPath(string path, long mediaId)
    {
        if (string.IsNullOrEmpty(path) || mediaId <= 0)
        {
            return null;
        }

        Match pickerMatch = Regex.Match(path, @"/\.transforms/synthetic/picker/(\d+)/([^/\\]+)/media/(\d+)(?:\.[^/\\]+)?$");
        if (!pickerMatch.Success || pickerMatch.Groups.Count < 4)
        {
            return null;
        }

        string pickerUser = pickerMatch.Groups[1].Value;
        string providerAuthority = pickerMatch.Groups[2].Value;
        string mediaIdFromPath = pickerMatch.Groups[3].Value;
        if (string.IsNullOrEmpty(pickerUser) || string.IsNullOrEmpty(providerAuthority))
        {
            return null;
        }

        string idPart = string.IsNullOrEmpty(mediaIdFromPath)
            ? mediaId.ToString(CultureInfo.InvariantCulture)
            : mediaIdFromPath;

        // Android Photo Picker uri format:
        // content://media/picker/<user>/<provider-authority>/media/<id>
        return "content://media/picker/" + pickerUser + "/" + providerAuthority + "/media/" + idPart;
    }

    private string QueryDisplayNameFromContentUri(string uriString)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaObject resolver = AndroidActivity.Call<AndroidJavaObject>("getContentResolver"))
            using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
            using (AndroidJavaObject contentUri = uriClass.CallStatic<AndroidJavaObject>("parse", uriString))
            {
                if (resolver == null || contentUri == null)
                {
                    return null;
                }

                string[] preferredProjection = { "_display_name", "display_name", "_data" };
                string fromPreferred = QueryDisplayNameFromCursor(resolver, contentUri, preferredProjection, null, null);
                if (!string.IsNullOrEmpty(fromPreferred))
                {
                    return fromPreferred;
                }

                // Last chance: ask for all columns and probe common name columns dynamically.
                return QueryDisplayNameFromCursor(resolver, contentUri, null, null, null);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: content uri display name lookup failed for '" + uriString + "'. " + ex.Message);
            return null;
        }
    #else
        return null;
    #endif
    }

    private bool IsLikelySyntheticPickerName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string trimmed = fileName.Trim();
        return Regex.IsMatch(trimmed, @"^\d{6,}\.[A-Za-z0-9]{2,5}$");
    }

    private string QueryDisplayNameFromCursor(
        AndroidJavaObject resolver,
        AndroidJavaObject contentUri,
        string[] projection,
        string selection,
        string[] selectionArgs)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (resolver == null || contentUri == null)
        {
            return null;
        }

        try
        {
            using (AndroidJavaObject cursor = resolver.Call<AndroidJavaObject>("query", contentUri, projection, selection, selectionArgs, null))
            {
                if (cursor == null || !cursor.Call<bool>("moveToFirst"))
                {
                    return null;
                }

                string value = ReadCursorColumnAsString(cursor, "_display_name");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }

                value = ReadCursorColumnAsString(cursor, "display_name");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }

                int columnCount = cursor.Call<int>("getColumnCount");
                for (int i = 0; i < columnCount; i++)
                {
                    string columnName = cursor.Call<string>("getColumnName", i);
                    if (string.IsNullOrEmpty(columnName))
                    {
                        continue;
                    }

                    string lower = columnName.ToLowerInvariant();
                    if (lower.Contains("display") || lower == "name")
                    {
                        value = cursor.Call<string>("getString", i);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value.Trim();
                        }
                    }
                }

                return null;
            }
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
    }

    private string ReadCursorColumnAsString(AndroidJavaObject cursor, string columnName)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (cursor == null || string.IsNullOrEmpty(columnName))
        {
            return null;
        }

        int columnIndex = cursor.Call<int>("getColumnIndex", columnName);
        if (columnIndex < 0)
        {
            return null;
        }

        try
        {
            return cursor.Call<string>("getString", columnIndex);
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
    }

    private long ExtractMediaStoreIdFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return -1;
        }

        string fileNameNoExt = Path.GetFileNameWithoutExtension(path);
        if (long.TryParse(fileNameNoExt, NumberStyles.Integer, CultureInfo.InvariantCulture, out long directId))
        {
            return directId;
        }

        Match match = Regex.Match(path, @"(?:/|\\)(\d+)(?:\.[^/\\]+)?$");
        if (match.Success &&
            long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long regexId))
        {
            return regexId;
        }

        return -1;
    }

    private long ExtractMediaStoreIdFromUri(string uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return -1;
        }

        Match match = Regex.Match(uri, @"/(\d+)$");
        if (match.Success &&
            long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
        {
            return id;
        }

        return -1;
    }

    private long TryGetFileSizeBytes(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return 0;
        }

        try
        {
            FileInfo fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return 0;
            }

            return fileInfo.Length;
        }
        catch
        {
            return 0;
        }
    }

    private string ResolveDisplayNameFromTagLib(string path)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using (TagLib.File tagFile = TagLib.File.Create(path))
            {
                string title = tagFile?.Tag?.Title;
                if (string.IsNullOrWhiteSpace(title))
                {
                    return null;
                }

                string extension = Path.GetExtension(path);
                string trimmed = title.Trim();
                if (string.IsNullOrEmpty(extension) || trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed;
                }

                return trimmed + extension;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: TagLib display name fallback failed. " + ex.Message);
            return null;
        }
#else
        return null;
#endif
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

    private void RequestReadPermissionViaNativeGallery(int mediaType, Action<bool> onCompleted)
    {
        onCompleted ??= delegate { };
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            int current = NativeGalleryClass.CallStatic<int>("CheckPermission", AndroidActivity, true, mediaType);
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
                mediaType);
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

    private void ConfigureNativeGalleryPickerForDisplayNames()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (nativeGalleryPickerConfigured)
        {
            return;
        }

        try
        {
            using (AndroidJavaClass pickerFragmentClass = new AndroidJavaClass("com.yasirkula.unity.NativeGalleryMediaPickerFragment"))
            {
                // Ensure picker Uris remain queryable after selection and preserve original names when copied.
                pickerFragmentClass.SetStatic("GrantPersistableUriPermission", true);
                pickerFragmentClass.SetStatic("tryPreserveFilenames", true);
                pickerFragmentClass.SetStatic("preferGetContent", true);
                pickerFragmentClass.SetStatic("useDefaultGalleryApp", false);
            }

            nativeGalleryPickerConfigured = true;
            Debug.Log("ManualAndroidVideoComposerUI: NativeGallery picker configured for display-name preservation.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: Failed to configure NativeGallery picker flags. " + ex.Message);
        }
#endif
    }

    private void PickVideosViaNativeGallery(bool multiple, string title, Action<string[]> onCompleted)
    {
        PickMediaViaNativeGallery(NativeGalleryMediaTypeVideo, multiple, "video/*", title, onCompleted);
    }

    private void PickAudiosViaNativeGallery(bool multiple, string title, Action<string[]> onCompleted)
    {
        PickMediaViaNativeGallery(NativeGalleryMediaTypeAudio, multiple, "audio/*", title, onCompleted);
    }

    private void PickMediaViaNativeGallery(int mediaType, bool multiple, string mime, string title, Action<string[]> onCompleted)
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
                mediaType,
                multiple,
                pickedMediaPath,
                string.IsNullOrWhiteSpace(mime) ? "*/*" : mime,
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

    private long GetAudioDurationMsViaMetadataRetriever(string path)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        AndroidJavaObject retriever = null;
        try
        {
            retriever = new AndroidJavaObject("android.media.MediaMetadataRetriever");
            if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
                using (AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>("parse", path))
                {
                    retriever.Call("setDataSource", AndroidActivity, uri);
                }
            }
            else
            {
                retriever.Call("setDataSource", path);
            }

            string duration = retriever.Call<string>("extractMetadata", 9);
            if (long.TryParse(duration, NumberStyles.Integer, CultureInfo.InvariantCulture, out long durationMs))
            {
                return Math.Max(0, durationMs);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ManualAndroidVideoComposerUI: audio duration read failed for '" + path + "'. " + ex.Message);
        }
        finally
        {
            if (retriever != null)
            {
                try
                {
                    retriever.Call("release");
                }
                catch
                {
                    // ignored
                }

                retriever.Dispose();
            }
        }

        return 0;
#else
        return 0;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private const string NativeGalleryClassName = "com.yasirkula.unity.NativeGallery";
    private const string UnityPlayerClassName = "com.unity3d.player.UnityPlayer";

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

    private void ApplyVideoToMatchingPlane(SpatialVideoMedia entry, int slot)
    {
        if (entry == null)
        {
            return;
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
        binding.videoPlayer.url = entry.sourceUri;
        binding.videoPlayer.playOnAwake = false;
        binding.videoPlayer.isLooping = true;
        binding.videoPlayer.Prepare();
        ApplyStereoModeToBinding(binding, projectionKey, entry.displayName);
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

        if (detection.StereoLayout != DetectedStereoLayout.Mono &&
            detection.StereoLayout != DetectedStereoLayout.Unknown &&
            (detection.Projection == DetectedProjection.Flat || detection.Projection == DetectedProjection.Unknown))
        {
            // Flat clips that carry SBS/TB stereo tags should use the stereoscopic flat plane.
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

    private void ApplyStereoModeToBinding(PlaneBinding binding, string projectionKey, string fileName)
    {
        if (binding == null)
        {
            return;
        }

        Renderer targetRenderer = binding.targetRenderer;
        if (targetRenderer == null && binding.planeObject != null)
        {
            targetRenderer = binding.planeObject.GetComponent<Renderer>();
        }

        if (targetRenderer == null)
        {
            return;
        }

        Material material = targetRenderer.material;
        if (material == null)
        {
            return;
        }

        if (!material.HasProperty(StereoModeShaderId))
        {
            Debug.LogWarning($"ManualAndroidVideoComposerUI: Renderer '{targetRenderer.gameObject.name}' material has no _StereoMode property. Stereo 3D cannot be forced.");
            return;
        }

        int stereoMode = ResolveStereoShaderMode(fileName, projectionKey);
        material.SetFloat(StereoModeShaderId, stereoMode);
        Debug.Log($"ManualAndroidVideoComposerUI: StereoMode={stereoMode} applied on '{targetRenderer.gameObject.name}' for '{fileName}'.");
    }

    private int ResolveStereoShaderMode(string fileName, string projectionKey)
    {
        FilenameVideoDetectionResult detection = FilenameVideoTypeDetector.Detect(fileName);
        if (detection != null)
        {
            switch (detection.StereoLayout)
            {
                case DetectedStereoLayout.LeftRight:
                    return 1;
                case DetectedStereoLayout.RightLeft:
                    return 2;
                case DetectedStereoLayout.TopBottom:
                    return 3;
                case DetectedStereoLayout.BottomTop:
                    return 4;
            }

            if (forceSbsStereoForFlat3DFallback &&
                string.Equals(projectionKey, "Flat3D", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }
        }

        if (forceSbsStereoForFlat3DFallback &&
            string.Equals(projectionKey, "Flat3D", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private SpatialProjectionType ToSpatialProjection(DetectedProjection projection)
    {
        switch (projection)
        {
            case DetectedProjection.VR180:
            case DetectedProjection.Fisheye180:
                return SpatialProjectionType.VR180;
            case DetectedProjection.Fisheye190:
                return SpatialProjectionType.VR190;
            case DetectedProjection.Fisheye200:
                return SpatialProjectionType.VR200;
            case DetectedProjection.Fisheye220:
                return SpatialProjectionType.VR220;
            case DetectedProjection.VR360:
            case DetectedProjection.EAC360:
                return SpatialProjectionType.VR360;
            case DetectedProjection.Flat:
                return SpatialProjectionType.Flat;
            default:
                return SpatialProjectionType.Unknown;
        }
    }

    private SpatialStereoLayout ToSpatialStereo(DetectedStereoLayout stereo)
    {
        switch (stereo)
        {
            case DetectedStereoLayout.Mono:
                return SpatialStereoLayout.Mono;
            case DetectedStereoLayout.LeftRight:
                return SpatialStereoLayout.SideBySideLeftRight;
            case DetectedStereoLayout.RightLeft:
                return SpatialStereoLayout.SideBySideRightLeft;
            case DetectedStereoLayout.TopBottom:
                return SpatialStereoLayout.TopBottom;
            case DetectedStereoLayout.BottomTop:
                return SpatialStereoLayout.BottomTop;
            default:
                return SpatialStereoLayout.Unknown;
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

        if (addVideoButton != null)
        {
            addVideoButton.interactable = interactable;
        }

        if (addAudioButton != null)
        {
            addAudioButton.interactable = interactable;
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

    private string BuildSelectedVideosStatus()
    {
        StringBuilder sb = new StringBuilder(256);
        sb.Append("Finished: selected videos (");
        sb.Append(spatialProject.videos.Count);
        sb.Append("), audios (");
        sb.Append(spatialProject.audios.Count);
        sb.Append(")");

        int startIndex = Mathf.Max(0, spatialProject.videos.Count - 6);
        for (int i = startIndex; i < spatialProject.videos.Count; i++)
        {
            SpatialVideoMedia entry = spatialProject.videos[i];
            if (entry == null)
            {
                continue;
            }

            sb.Append('\n');
            sb.Append(i + 1);
            sb.Append(") ");
            sb.Append(entry.displayName);
        }

        int audioStartIndex = Mathf.Max(0, spatialProject.audios.Count - 4);
        for (int i = audioStartIndex; i < spatialProject.audios.Count; i++)
        {
            SpatialAudioMedia entry = spatialProject.audios[i];
            if (entry == null)
            {
                continue;
            }

            sb.Append('\n');
            sb.Append("A");
            sb.Append(i + 1);
            sb.Append(") ");
            sb.Append(entry.displayName);
        }

        return sb.ToString();
    }

    private string BuildOutputFileName(IReadOnlyList<SpatialVideoMedia> inputs)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string stereoTag = ResolveOutputStereoTag(inputs);
        return $"SpatialEditor_{timestamp}_{stereoTag}.mp4";
    }

    private string ResolveOutputStereoTag(IReadOnlyList<SpatialVideoMedia> inputs)
    {
        SpatialStereoLayout layout = SpatialStereoLayout.Unknown;
        string firstName = null;
        if (inputs != null && inputs.Count > 0 && inputs[0] != null)
        {
            layout = inputs[0].stereoLayout;
            firstName = inputs[0].displayName;
        }

        if (layout == SpatialStereoLayout.Unknown && !string.IsNullOrWhiteSpace(firstName))
        {
            FilenameVideoDetectionResult detection = FilenameVideoTypeDetector.Detect(firstName);
            if (detection != null)
            {
                layout = ToSpatialStereo(detection.StereoLayout);
            }
        }

        string tag;
        switch (layout)
        {
            case SpatialStereoLayout.Mono:
                tag = "mono";
                break;
            case SpatialStereoLayout.SideBySideLeftRight:
                tag = "sbs_lr";
                break;
            case SpatialStereoLayout.SideBySideRightLeft:
                tag = "sbs_rl";
                break;
            case SpatialStereoLayout.TopBottom:
                tag = "tb";
                break;
            case SpatialStereoLayout.BottomTop:
                tag = "bt";
                break;
            default:
                tag = forceSbsStereoForFlat3DFallback ? "sbs_lr" : "unknown";
                break;
        }

        return SanitizeBaseName(tag).ToLowerInvariant();
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
