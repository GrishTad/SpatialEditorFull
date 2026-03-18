using System;
using System.Collections.Generic;
using UnityEngine;

public enum SpatialStereoLayout
{
    Unknown = 0,
    Mono = 1,
    SideBySideLeftRight = 2,
    SideBySideRightLeft = 3,
    TopBottom = 4,
    BottomTop = 5,
}

public enum SpatialProjectionType
{
    Unknown = 0,
    Flat = 1,
    VR180 = 2,
    VR190 = 3,
    VR200 = 4,
    VR220 = 5,
    VR360 = 6,
}

public enum SpatialTransitionType
{
    None = 0,
    FadeFromBlack = 1,
    DipToBlack = 2,
}

[Serializable]
public sealed class SpatialTransition
{
    public SpatialTransitionType type = SpatialTransitionType.None;
    public long durationMs = 0;
}

[Serializable]
public sealed class SpatialVideoEditSettings
{
    public long trimStartMs = 0;
    public long trimEndMs = 0;
    public bool isAudioMuted = false;
    public double audioVolume = 1.0;

    public double brightness = 0.0;
    public double contrast = 1.0;
    public double saturation = 1.0;
    public string lut;
    public string[] overlayIds = Array.Empty<string>();

    public SpatialTransition transitionIn = new SpatialTransition();
    public SpatialTransition transitionOut = new SpatialTransition();
}

[Serializable]
public sealed class SpatialAudioEditSettings
{
    public long trimStartMs = 0;
    public long trimEndMs = 0;
    public bool loop = false;
    public double volume = 1.0;

    // Current Android export schema does not expose absolute timeline offset for audio tracks.
    // Kept here so Unity-side project structure can evolve without breaking data contracts.
    public long sequenceStartMs = 0;
}

[Serializable]
public sealed class SpatialVideoMedia
{
    public string id;
    public string sourceUri;
    public string displayName;
    public long durationMs;
    public long sizeBytes;
    public string mimeType = "video/*";
    public long dateAddedSec;

    public SpatialProjectionType projectionType = SpatialProjectionType.Flat;
    public SpatialStereoLayout stereoLayout = SpatialStereoLayout.Unknown;

    public SpatialVideoEditSettings edits = new SpatialVideoEditSettings();
}

[Serializable]
public sealed class SpatialAudioMedia
{
    public string id;
    public string sourceUri;
    public string displayName;
    public long durationMs;
    public long sizeBytes;
    public string mimeType = "audio/*";
    public long dateAddedSec;

    public SpatialAudioEditSettings edits = new SpatialAudioEditSettings();
}

[Serializable]
public sealed class SpatialProjectExportSettings
{
    public string outputUri;
    public string videoMimeType = "video/avc";
    public string audioMimeType = "audio/mp4a-latm";
    public int width = 1920;
    public int height = 1080;
    public int fps = 30;
}

[Serializable]
public sealed class SpatialProject
{
    public int version = 1;
    public List<SpatialVideoMedia> videos = new List<SpatialVideoMedia>();
    public List<SpatialAudioMedia> audios = new List<SpatialAudioMedia>();
    public SpatialProjectExportSettings export = new SpatialProjectExportSettings();
}

public static class SpatialProjectMapper
{
    public static AndroidProjectDocument ToAndroidProjectDocument(SpatialProject project)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        List<AndroidAssetRef> assets = new List<AndroidAssetRef>(project.videos.Count + project.audios.Count);
        List<AndroidVideoClipSpec> videoTrack = new List<AndroidVideoClipSpec>(project.videos.Count);
        List<AndroidAudioClipSpec> audioTrack = new List<AndroidAudioClipSpec>(project.audios.Count);
        int clipFps = Mathf.Max(1, project.export == null ? 30 : project.export.fps);

        for (int i = 0; i < project.videos.Count; i++)
        {
            SpatialVideoMedia video = project.videos[i];
            if (video == null || string.IsNullOrEmpty(video.sourceUri))
            {
                continue;
            }

            string id = string.IsNullOrEmpty(video.id) ? ("v" + (i + 1)) : video.id;
            long startMs = Math.Max(0, video.edits == null ? 0 : video.edits.trimStartMs);
            long fallbackEndMs = video.durationMs > startMs ? video.durationMs : (startMs + 1000);
            long endMs = Math.Max(startMs + 1, video.edits == null ? fallbackEndMs : (video.edits.trimEndMs > 0 ? video.edits.trimEndMs : fallbackEndMs));

            SpatialVideoEditSettings edits = video.edits ?? new SpatialVideoEditSettings();
            string[] overlayIds = edits.overlayIds ?? Array.Empty<string>();

            assets.Add(new AndroidAssetRef
            {
                id = id,
                type = "video",
                uri = video.sourceUri,
            });

            videoTrack.Add(new AndroidVideoClipSpec
            {
                assetId = id,
                trimStartMs = startMs,
                trimEndMs = endMs,
                durationMs = video.durationMs,
                frameRate = clipFps,
                removeAudio = edits.isAudioMuted,
                volume = edits.audioVolume,
                effects = new AndroidClipEffectsSpec
                {
                    brightness = edits.brightness,
                    contrast = edits.contrast,
                    saturation = edits.saturation,
                    lut = string.IsNullOrWhiteSpace(edits.lut) ? null : edits.lut,
                    overlayIds = overlayIds,
                },
                transitionIn = ToAndroidTransition(edits.transitionIn),
                transitionOut = ToAndroidTransition(edits.transitionOut),
            });
        }

        for (int i = 0; i < project.audios.Count; i++)
        {
            SpatialAudioMedia audio = project.audios[i];
            if (audio == null || string.IsNullOrEmpty(audio.sourceUri))
            {
                continue;
            }

            string id = string.IsNullOrEmpty(audio.id) ? ("a" + (i + 1)) : audio.id;
            SpatialAudioEditSettings edits = audio.edits ?? new SpatialAudioEditSettings();
            long startMs = Math.Max(0, edits.trimStartMs);
            long fallbackEndMs = audio.durationMs > startMs ? audio.durationMs : (startMs + 1000);
            long endMs = Math.Max(startMs + 1, edits.trimEndMs > 0 ? edits.trimEndMs : fallbackEndMs);

            assets.Add(new AndroidAssetRef
            {
                id = id,
                type = "audio",
                uri = audio.sourceUri,
            });

            audioTrack.Add(new AndroidAudioClipSpec
            {
                assetId = id,
                trimStartMs = startMs,
                trimEndMs = endMs,
                loop = edits.loop,
                volume = edits.volume,
            });
        }

        SpatialProjectExportSettings export = project.export ?? new SpatialProjectExportSettings();
        AndroidExportSpec exportSpec = new AndroidExportSpec
        {
            outputUri = export.outputUri,
            videoMimeType = string.IsNullOrWhiteSpace(export.videoMimeType) ? "video/avc" : export.videoMimeType,
            audioMimeType = string.IsNullOrWhiteSpace(export.audioMimeType) ? "audio/mp4a-latm" : export.audioMimeType,
            width = Mathf.Max(2, export.width),
            height = Mathf.Max(2, export.height),
            fps = Mathf.Max(1, export.fps),
        };

        return new AndroidProjectDocument
        {
            version = project.version <= 0 ? 1 : project.version,
            assets = assets.ToArray(),
            videoTrack = videoTrack.ToArray(),
            audioTracks = audioTrack.ToArray(),
            overlays = Array.Empty<AndroidOverlaySpec>(),
            export = exportSpec,
        };
    }

    public static string ToProjectJson(SpatialProject project)
    {
        AndroidProjectDocument doc = ToAndroidProjectDocument(project);
        return JsonUtility.ToJson(doc);
    }

    private static AndroidTransitionSpec ToAndroidTransition(SpatialTransition transition)
    {
        if (transition == null || transition.durationMs <= 0)
        {
            return null;
        }

        string type;
        switch (transition.type)
        {
            case SpatialTransitionType.FadeFromBlack:
                type = "fade_from_black";
                break;
            case SpatialTransitionType.DipToBlack:
                type = "dip_to_black";
                break;
            default:
                type = null;
                break;
        }

        if (string.IsNullOrEmpty(type))
        {
            return null;
        }

        return new AndroidTransitionSpec
        {
            type = type,
            durationMs = Math.Max(1, transition.durationMs),
        };
    }
}
