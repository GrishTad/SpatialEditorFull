using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

public static class SampleSceneRuntimeBootstrap
{
    private const string CustomVideoShaderName = "Custom/VRVideoSurface_URP";
    private const string UrpUnlitShaderName = "Universal Render Pipeline/Unlit";
    private const string LegacyUnlitShaderName = "Unlit/Texture";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureSampleSceneSetup()
    {
        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid() || active.name != "SampleScene")
        {
            return;
        }

        VRVideoSurfaceController controller = Object.FindAnyObjectByType<VRVideoSurfaceController>();
        GameObject rig;
        if (controller == null)
        {
            rig = new GameObject("VRVideoSurfaceRig_Runtime");
            controller = rig.AddComponent<VRVideoSurfaceController>();
        }
        else
        {
            rig = controller.gameObject;
        }

        VideoPlayer player = rig.GetComponent<VideoPlayer>();
        if (player == null)
        {
            player = rig.AddComponent<VideoPlayer>();
        }

        Material material = CreateVideoSurfaceMaterial();

        GameObject flat = FindOrCreateFlatSurface(rig.transform);
        GameObject sphere180 = FindOrCreateMeshSurface("Sphere180", "VideoPlaneMeshes/180 sphere", rig.transform, 180f);
        GameObject sphere190 = FindOrCreateMeshSurface("Sphere190", "VideoPlaneMeshes/190 sphere", rig.transform, 190f);
        GameObject sphere200 = FindOrCreateMeshSurface("Sphere200", "VideoPlaneMeshes/200 sphere", rig.transform, 200f);
        GameObject sphere220 = FindOrCreateMeshSurface("Sphere220", "VideoPlaneMeshes/220 sphere", rig.transform, 220f);
        GameObject sphere360 = FindOrCreateMeshSurface("Sphere360", "VideoPlaneMeshes/360 Sphere", rig.transform, 360f);
        ResetLocalRotationIdentity(flat, sphere180, sphere190, sphere200, sphere220, sphere360);
        ResetLocalScaleIdentity(sphere180, sphere190, sphere200, sphere220, sphere360);

        Renderer flatR = flat.GetComponent<Renderer>();
        Renderer r180 = sphere180.GetComponent<Renderer>();
        Renderer r190 = sphere190.GetComponent<Renderer>();
        Renderer r200 = sphere200.GetComponent<Renderer>();
        Renderer r220 = sphere220.GetComponent<Renderer>();
        Renderer r360 = sphere360.GetComponent<Renderer>();

        ApplyMaterial(material, flatR, r180, r190, r200, r220, r360);

        controller.SetVideoSource(player);
        controller.SetSurfaceBindings(
            flat,
            sphere180,
            sphere190,
            sphere200,
            sphere220,
            sphere360,
            flatR,
            r180,
            r190,
            r200,
            r220,
            r360);

        // Manual UI flow now controls picking and export.
    }

    private static GameObject FindOrCreateFlatSurface(Transform parent)
    {
        GameObject existing = GameObject.Find("FlatSurface");
        if (existing != null)
        {
            return existing;
        }

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "FlatSurface";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0f, 3f);
        go.transform.localRotation = Quaternion.identity;
        go.SetActive(false);
        return go;
    }

    private static GameObject FindOrCreateMeshSurface(string name, string resourcePath, Transform parent, float horizontalFov)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
        {
            return existing;
        }

        GameObject go = TryInstantiateModelPrefab(resourcePath, parent);
        if (go == null)
        {
            go = TryCreateFromResourceMesh(resourcePath, parent);
        }

        if (go == null)
        {
            go = CreateFallbackSphere(parent, horizontalFov);
        }

        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.SetActive(false);
        return go;
    }

    private static void ResetLocalRotationIdentity(params GameObject[] surfaces)
    {
        if (surfaces == null)
        {
            return;
        }

        for (int i = 0; i < surfaces.Length; i++)
        {
            GameObject surface = surfaces[i];
            if (surface != null)
            {
                surface.transform.localEulerAngles = Vector3.zero;
            }
        }
    }

    private static GameObject TryInstantiateModelPrefab(string resourcePath, Transform parent)
    {
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            return null;
        }

        GameObject instance = Object.Instantiate(prefab, parent, false);
        if (instance.GetComponent<Renderer>() == null || instance.GetComponent<MeshFilter>() == null)
        {
            Object.Destroy(instance);
            return null;
        }
        return instance;
    }

    private static GameObject TryCreateFromResourceMesh(string resourcePath, Transform parent)
    {
        Mesh mesh = Resources.Load<Mesh>(resourcePath);
        if (mesh == null)
        {
            return null;
        }

        GameObject go = new GameObject("MeshSurface");
        go.transform.SetParent(parent, false);
        MeshFilter meshFilter = go.AddComponent<MeshFilter>();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        meshFilter.sharedMesh = mesh;
        renderer.sharedMaterial = null;
        return go;
    }

    private static GameObject CreateFallbackSphere(Transform parent, float horizontalFov)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one;
        float cropRatio = Mathf.Clamp01(horizontalFov / 360f);
        if (cropRatio < 0.999f)
        {
            MeshFilter meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                meshFilter.sharedMesh = CloneAndCropSphereUv(meshFilter.sharedMesh, cropRatio);
            }
        }
        return go;
    }

    private static Material CreateVideoSurfaceMaterial()
    {
        Shader shader = Shader.Find(CustomVideoShaderName);
        if (shader != null && shader.isSupported)
        {
            return new Material(shader);
        }

        shader = Shader.Find(UrpUnlitShaderName);
        if (shader != null && shader.isSupported)
        {
            return new Material(shader);
        }

        shader = Shader.Find(LegacyUnlitShaderName);
        return shader != null && shader.isSupported ? new Material(shader) : null;
    }

    private static void ApplyMaterial(Material material, params Renderer[] renderers)
    {
        if (material == null || renderers == null)
        {
            return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }
    }

    private static void ResetLocalScaleIdentity(params GameObject[] surfaces)
    {
        if (surfaces == null)
        {
            return;
        }

        for (int i = 0; i < surfaces.Length; i++)
        {
            GameObject surface = surfaces[i];
            if (surface != null)
            {
                surface.transform.localScale = Vector3.one;
            }
        }
    }

    private static Mesh CloneAndCropSphereUv(Mesh source, float cropRatio)
    {
        if (source == null)
        {
            return null;
        }

        Mesh clone = Object.Instantiate(source);
        Vector2[] uvs = clone.uv;
        if (uvs != null && uvs.Length > 0)
        {
            for (int i = 0; i < uvs.Length; i++)
            {
                Vector2 uv = uvs[i];
                uv.x = Mathf.Lerp(0.5f - cropRatio * 0.5f, 0.5f + cropRatio * 0.5f, uv.x);
                uvs[i] = uv;
            }
            clone.uv = uvs;
        }
        return clone;
    }
}
