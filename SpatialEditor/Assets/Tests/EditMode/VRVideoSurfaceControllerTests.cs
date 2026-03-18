using System;
using NUnit.Framework;
using UnityEngine;

public class VRVideoSurfaceControllerTests
{
    [Test]
    public void ActivatesOnlySelectedProjectionSurface()
    {
        Type controllerType = ResolveType("VRVideoSurfaceController");
        Assert.NotNull(controllerType, "VRVideoSurfaceController type not found.");

        GameObject root = new GameObject("surface-test");
        try
        {
            Component controller = root.AddComponent(controllerType);
            Shader shader = Shader.Find("Unlit/Texture")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Standard");
            Assert.NotNull(shader);
            Material material = new Material(shader);

            GameObject flat = CreateSurface("FlatSurface", root.transform, material);
            GameObject s180 = CreateSurface("Sphere180", root.transform, material);
            GameObject s190 = CreateSurface("Sphere190", root.transform, material);
            GameObject s200 = CreateSurface("Sphere200", root.transform, material);
            GameObject s220 = CreateSurface("Sphere220", root.transform, material);
            GameObject s360 = CreateSurface("Sphere360", root.transform, material);

            controllerType.GetMethod("SetSurfaceBindings")?.Invoke(
                controller,
                new object[]
                {
                    flat, s180, s190, s200, s220, s360,
                    flat.GetComponent<Renderer>(),
                    s180.GetComponent<Renderer>(),
                    s190.GetComponent<Renderer>(),
                    s200.GetComponent<Renderer>(),
                    s220.GetComponent<Renderer>(),
                    s360.GetComponent<Renderer>(),
                });

            InvokeApply(controllerType, controller, "VR220");
            Assert.IsFalse(flat.activeSelf);
            Assert.IsFalse(s180.activeSelf);
            Assert.IsFalse(s190.activeSelf);
            Assert.IsFalse(s200.activeSelf);
            Assert.IsTrue(s220.activeSelf);
            Assert.IsFalse(s360.activeSelf);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SupportsAllProjectionModes()
    {
        Type controllerType = ResolveType("VRVideoSurfaceController");
        Assert.NotNull(controllerType, "VRVideoSurfaceController type not found.");

        GameObject root = new GameObject("projection-test");
        try
        {
            Component controller = root.AddComponent(controllerType);
            Shader shader = Shader.Find("Unlit/Texture")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Standard");
            Assert.NotNull(shader);
            Material material = new Material(shader);

            GameObject flat = CreateSurface("FlatSurface", root.transform, material);
            GameObject s180 = CreateSurface("Sphere180", root.transform, material);
            GameObject s190 = CreateSurface("Sphere190", root.transform, material);
            GameObject s200 = CreateSurface("Sphere200", root.transform, material);
            GameObject s220 = CreateSurface("Sphere220", root.transform, material);
            GameObject s360 = CreateSurface("Sphere360", root.transform, material);

            controllerType.GetMethod("SetSurfaceBindings")?.Invoke(
                controller,
                new object[]
                {
                    flat, s180, s190, s200, s220, s360,
                    flat.GetComponent<Renderer>(),
                    s180.GetComponent<Renderer>(),
                    s190.GetComponent<Renderer>(),
                    s200.GetComponent<Renderer>(),
                    s220.GetComponent<Renderer>(),
                    s360.GetComponent<Renderer>(),
                });

            AssertProjection(controllerType, controller, flat, s180, s190, s200, s220, s360, "Flat", flat);
            AssertProjection(controllerType, controller, flat, s180, s190, s200, s220, s360, "VR180", s180);
            AssertProjection(controllerType, controller, flat, s180, s190, s200, s220, s360, "VR190", s190);
            AssertProjection(controllerType, controller, flat, s180, s190, s200, s220, s360, "VR200", s200);
            AssertProjection(controllerType, controller, flat, s180, s190, s200, s220, s360, "VR220", s220);
            AssertProjection(controllerType, controller, flat, s180, s190, s200, s220, s360, "VR360", s360);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void AssertProjection(
        Type controllerType,
        Component controller,
        GameObject flat,
        GameObject s180,
        GameObject s190,
        GameObject s200,
        GameObject s220,
        GameObject s360,
        string projectionName,
        GameObject expectedActive)
    {
        InvokeApply(controllerType, controller, projectionName);
        GameObject[] surfaces = { flat, s180, s190, s200, s220, s360 };
        for (int i = 0; i < surfaces.Length; i++)
        {
            GameObject surface = surfaces[i];
            if (surface == expectedActive)
            {
                Assert.IsTrue(surface.activeSelf, $"Expected active: {surface.name}");
            }
            else
            {
                Assert.IsFalse(surface.activeSelf, $"Expected inactive: {surface.name}");
            }
        }
    }

    private static void InvokeApply(Type controllerType, Component controller, string projectionName)
    {
        Type projectionEnum = controllerType.GetNestedType("ProjectionType");
        Type stereoEnum = controllerType.GetNestedType("StereoLayout");
        Assert.NotNull(projectionEnum);
        Assert.NotNull(stereoEnum);

        object projection = Enum.Parse(projectionEnum, projectionName);
        object mono = Enum.Parse(stereoEnum, "Mono");
        controllerType.GetMethod("Apply", new[] { projectionEnum, stereoEnum, typeof(Texture) })?.Invoke(
            controller,
            new[] { projection, mono, null });
    }

    private static GameObject CreateSurface(string name, Transform parent, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(parent, false);
        Renderer renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return go;
    }

    private static Type ResolveType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}

