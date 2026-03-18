using System;
using NUnit.Framework;

public class FilenameVideoTypeDetectorTests
{
    [Test]
    public void DetectsFisheye190LeftRight()
    {
        object result = Detect("clip_vr190_lr.mp4");
        Assert.AreEqual("Fisheye190", ReadEnumFieldName(result, "Projection"));
        Assert.AreEqual("LeftRight", ReadEnumFieldName(result, "StereoLayout"));
    }

    [Test]
    public void DetectsFisheye200TopBottom()
    {
        object result = Detect("travel_mkx200_tb.mov");
        Assert.AreEqual("Fisheye200", ReadEnumFieldName(result, "Projection"));
        Assert.AreEqual("TopBottom", ReadEnumFieldName(result, "StereoLayout"));
    }

    [Test]
    public void DetectsFisheye220MonoWhenNoStereoTag()
    {
        object result = Detect("scene_vrca220.mp4");
        Assert.AreEqual("Fisheye220", ReadEnumFieldName(result, "Projection"));
        Assert.AreEqual("Mono", ReadEnumFieldName(result, "StereoLayout"));
    }

    private static object Detect(string filename)
    {
        Type detectorType = ResolveType("FilenameVideoTypeDetector");
        Assert.NotNull(detectorType, "FilenameVideoTypeDetector type not found.");
        var detectMethod = detectorType.GetMethod("Detect");
        Assert.NotNull(detectMethod);
        object result = detectMethod.Invoke(null, new object[] { filename });
        Assert.NotNull(result);
        return result;
    }

    private static string ReadEnumFieldName(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName);
        Assert.NotNull(field);
        object value = field.GetValue(instance);
        Assert.NotNull(value);
        return value.ToString();
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

