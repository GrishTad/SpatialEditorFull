using UnityEngine;
using UnityEngine.Video;

public class VRVideoSurfaceController : MonoBehaviour
{
    public enum ProjectionType
    {
        Flat,
        VR180,
        VR190,
        VR200,
        VR220,
        VR360,
    }

    public enum StereoLayout
    {
        Mono = 0,
        SideBySide_LeftRight = 1,
        SideBySide_RightLeft = 2,
        TopBottom = 3,
        BottomTop = 4,
    }

    [Header("Optional Video Source")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private Texture fallbackTexture;

    [Header("Surfaces")]
    [SerializeField] private GameObject flatSurface;
    [SerializeField] private GameObject sphere180Surface;
    [SerializeField] private GameObject sphere190Surface;
    [SerializeField] private GameObject sphere200Surface;
    [SerializeField] private GameObject sphere220Surface;
    [SerializeField] private GameObject sphere360Surface;

    [Header("Renderers")]
    [SerializeField] private Renderer flatRenderer;
    [SerializeField] private Renderer sphere180Renderer;
    [SerializeField] private Renderer sphere190Renderer;
    [SerializeField] private Renderer sphere200Renderer;
    [SerializeField] private Renderer sphere220Renderer;
    [SerializeField] private Renderer sphere360Renderer;

    [Header("Flat Screen Settings")]
    [Tooltip("Use a Quad, not Unity Plane, for simplest scaling.")]
    [SerializeField] private float flatHeight = 2.0f;
    [SerializeField] private float flatDistance = 3.0f;
    [SerializeField] private bool moveFlatSurfaceToDistance = true;

    [Header("Video Type")]
    [SerializeField] private ProjectionType projectionType = ProjectionType.Flat;
    [SerializeField] private StereoLayout stereoLayout = StereoLayout.Mono;

    [Header("Shader Controls")]
    [SerializeField] private bool flipX;
    [SerializeField] private bool flipY;
    [SerializeField] private float yawOffsetDegrees;

    [Header("Auto Apply")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool autoApplyWhenPrepared = true;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int StereoModeId = Shader.PropertyToID("_StereoMode");
    private static readonly int FlipXId = Shader.PropertyToID("_FlipX");
    private static readonly int FlipYId = Shader.PropertyToID("_FlipY");
    private static readonly int YawOffsetDegreesId = Shader.PropertyToID("_YawOffsetDegrees");

    private void Awake()
    {
        if (videoPlayer != null && autoApplyWhenPrepared)
        {
            videoPlayer.prepareCompleted += OnVideoPrepared;
        }
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
        }
    }

    private void Start()
    {
        if (applyOnStart)
        {
            ApplyCurrentSelection();
        }
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        ApplyCurrentSelection();
    }

    [ContextMenu("Apply Current Selection")]
    public void ApplyCurrentSelection()
    {
        Texture tex = GetCurrentTexture();
        Apply(projectionType, stereoLayout, tex);
    }

    public void Apply(ProjectionType projection, StereoLayout stereo, Texture texture)
    {
        projectionType = projection;
        stereoLayout = stereo;

        SetActiveSurface(projectionType);

        ApplyTextureAndShaderParams(flatRenderer, texture, stereoLayout);
        ApplyTextureAndShaderParams(sphere180Renderer, texture, stereoLayout);
        ApplyTextureAndShaderParams(sphere190Renderer, texture, stereoLayout);
        ApplyTextureAndShaderParams(sphere200Renderer, texture, stereoLayout);
        ApplyTextureAndShaderParams(sphere220Renderer, texture, stereoLayout);
        ApplyTextureAndShaderParams(sphere360Renderer, texture, stereoLayout);

        if (projectionType == ProjectionType.Flat)
        {
            UpdateFlatSurfaceScaleAndPosition(texture, stereoLayout);
        }
    }

    public void SetVideoType(ProjectionType projection, StereoLayout stereo)
    {
        projectionType = projection;
        stereoLayout = stereo;
        ApplyCurrentSelection();
    }

    public void SetVideoSource(VideoPlayer source)
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
        }

        videoPlayer = source;

        if (videoPlayer != null && autoApplyWhenPrepared)
        {
            videoPlayer.prepareCompleted += OnVideoPrepared;
        }
    }

    public void SetSurfaceBindings(
        GameObject flat,
        GameObject s180,
        GameObject s190,
        GameObject s200,
        GameObject s220,
        GameObject s360,
        Renderer flatR,
        Renderer r180,
        Renderer r190,
        Renderer r200,
        Renderer r220,
        Renderer r360)
    {
        flatSurface = flat;
        sphere180Surface = s180;
        sphere190Surface = s190;
        sphere200Surface = s200;
        sphere220Surface = s220;
        sphere360Surface = s360;

        flatRenderer = flatR;
        sphere180Renderer = r180;
        sphere190Renderer = r190;
        sphere200Renderer = r200;
        sphere220Renderer = r220;
        sphere360Renderer = r360;
    }

    public void ApplyDetection(FilenameVideoDetectionResult detection, Texture texture = null)
    {
        if (detection == null)
        {
            ApplyCurrentSelection();
            return;
        }

        ProjectionType projection = ToProjectionType(detection.Projection);
        StereoLayout stereo = ToStereoLayout(detection.StereoLayout);
        Apply(projection, stereo, texture ?? GetCurrentTexture());
    }

    private ProjectionType ToProjectionType(DetectedProjection projection)
    {
        switch (projection)
        {
            case DetectedProjection.VR180:
            case DetectedProjection.Fisheye180:
                return ProjectionType.VR180;
            case DetectedProjection.Fisheye190:
                return ProjectionType.VR190;
            case DetectedProjection.Fisheye200:
                return ProjectionType.VR200;
            case DetectedProjection.Fisheye220:
                return ProjectionType.VR220;
            case DetectedProjection.VR360:
            case DetectedProjection.EAC360:
                return ProjectionType.VR360;
            default:
                return ProjectionType.Flat;
        }
    }

    private StereoLayout ToStereoLayout(DetectedStereoLayout layout)
    {
        switch (layout)
        {
            case DetectedStereoLayout.LeftRight:
                return StereoLayout.SideBySide_LeftRight;
            case DetectedStereoLayout.RightLeft:
                return StereoLayout.SideBySide_RightLeft;
            case DetectedStereoLayout.TopBottom:
                return StereoLayout.TopBottom;
            case DetectedStereoLayout.BottomTop:
                return StereoLayout.BottomTop;
            default:
                return StereoLayout.Mono;
        }
    }

    private Texture GetCurrentTexture()
    {
        if (videoPlayer != null)
        {
            if (videoPlayer.targetTexture != null)
            {
                return videoPlayer.targetTexture;
            }

            if (videoPlayer.texture != null)
            {
                return videoPlayer.texture;
            }
        }

        return fallbackTexture;
    }

    private void SetActiveSurface(ProjectionType projection)
    {
        if (flatSurface != null)
        {
            flatSurface.SetActive(projection == ProjectionType.Flat);
        }

        if (sphere180Surface != null)
        {
            sphere180Surface.SetActive(projection == ProjectionType.VR180);
        }

        if (sphere190Surface != null)
        {
            sphere190Surface.SetActive(projection == ProjectionType.VR190);
        }

        if (sphere200Surface != null)
        {
            sphere200Surface.SetActive(projection == ProjectionType.VR200);
        }

        if (sphere220Surface != null)
        {
            sphere220Surface.SetActive(projection == ProjectionType.VR220);
        }

        if (sphere360Surface != null)
        {
            sphere360Surface.SetActive(projection == ProjectionType.VR360);
        }
    }

    private void ApplyTextureAndShaderParams(Renderer rend, Texture texture, StereoLayout stereo)
    {
        if (rend == null)
        {
            return;
        }

        Material mat = rend.material;

        if (texture != null)
        {
            if (mat.HasProperty(BaseMapId))
            {
                mat.SetTexture(BaseMapId, texture);
            }
            else if (mat.HasProperty(MainTexId))
            {
                mat.SetTexture(MainTexId, texture);
            }
        }

        if (mat.HasProperty(StereoModeId))
        {
            mat.SetFloat(StereoModeId, (float)stereo);
        }
        if (mat.HasProperty(FlipXId))
        {
            mat.SetFloat(FlipXId, flipX ? 1f : 0f);
        }
        if (mat.HasProperty(FlipYId))
        {
            mat.SetFloat(FlipYId, flipY ? 1f : 0f);
        }

        float yaw = projectionType == ProjectionType.Flat ? 0f : yawOffsetDegrees;
        if (mat.HasProperty(YawOffsetDegreesId))
        {
            mat.SetFloat(YawOffsetDegreesId, yaw);
        }
    }

    private void UpdateFlatSurfaceScaleAndPosition(Texture texture, StereoLayout stereo)
    {
        if (flatSurface == null)
        {
            return;
        }

        Transform t = flatSurface.transform;

        if (moveFlatSurfaceToDistance)
        {
            t.localPosition = new Vector3(0f, 0f, flatDistance);
        }

        float aspect = GetPerEyeAspect(texture, stereo);
        t.localScale = new Vector3(aspect * flatHeight, flatHeight, 1f);
    }

    private float GetPerEyeAspect(Texture texture, StereoLayout stereo)
    {
        int width = 1920;
        int height = 1080;

        if (texture != null)
        {
            width = Mathf.Max(1, texture.width);
            height = Mathf.Max(1, texture.height);
        }

        float w = width;
        float h = height;

        switch (stereo)
        {
            case StereoLayout.SideBySide_LeftRight:
            case StereoLayout.SideBySide_RightLeft:
                return (w * 0.5f) / h;
            case StereoLayout.TopBottom:
            case StereoLayout.BottomTop:
                return w / (h * 0.5f);
            default:
                return w / h;
        }
    }
}
