using System;

[Serializable]
public class AndroidProjectDocument
{
    public int version = 1;
    public AndroidAssetRef[] assets;
    public AndroidVideoClipSpec[] videoTrack;
    public AndroidAudioClipSpec[] audioTracks;
    public AndroidOverlaySpec[] overlays;
    public AndroidExportSpec export;
}

[Serializable]
public class AndroidAssetRef
{
    public string id;
    public string type;
    public string uri;
}

[Serializable]
public class AndroidVideoClipSpec
{
    public string assetId;
    public long trimStartMs;
    public long trimEndMs;
    public long durationMs;
    public int frameRate = 30;
    public bool removeAudio;
    public double volume = 1.0;
    public AndroidClipEffectsSpec effects;
    public AndroidTransitionSpec transitionIn;
    public AndroidTransitionSpec transitionOut;
}

[Serializable]
public class AndroidAudioClipSpec
{
    public string assetId;
    public long trimStartMs;
    public long trimEndMs;
    public bool loop;
    public double volume = 1.0;
}

[Serializable]
public class AndroidClipEffectsSpec
{
    public double brightness;
    public double contrast = 1.0;
    public double saturation = 1.0;
    public string lut;
    public string[] overlayIds;
}

[Serializable]
public class AndroidTransitionSpec
{
    public string type;
    public long durationMs;
}

[Serializable]
public class AndroidOverlaySpec
{
    public string id;
    public string uri;
    public double x;
    public double y;
    public double scale;
    public double opacity = 1.0;
    public int zIndex;
}

[Serializable]
public class AndroidExportSpec
{
    public string outputUri;
    public string videoMimeType = "video/avc";
    public string audioMimeType = "audio/mp4a-latm";
    public int width = 1920;
    public int height = 1080;
    public int fps = 30;
}
