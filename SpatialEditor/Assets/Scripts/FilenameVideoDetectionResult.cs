using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public enum DetectedStereoLayout
{
    Unknown,
    Mono,
    LeftRight,   // SBS, LR
    RightLeft,   // RL
    TopBottom,   // TB, OU
    BottomTop    // BT
}

public enum DetectedProjection
{
    Unknown,
    Flat,
    VR180,
    VR360,
    Fisheye180,
    Fisheye190,
    Fisheye200,
    Fisheye220,
    EAC360
}

public enum DetectedVideoType
{
    Unknown,

    Flat2D,
    Flat3D,

    VR1802D,
    VR1803D,

    VR3602D,
    VR3603D,

    Fisheye1802D,
    Fisheye1803D,

    Fisheye1902D,
    Fisheye1903D,

    Fisheye2002D,
    Fisheye2003D,

    Fisheye2202D,
    Fisheye2203D,

    EAC3602D,
    EAC3603D
}

[Serializable]
public sealed class FilenameVideoDetectionResult
{
    public string OriginalFileName;
    public string FileNameWithoutExtension;

    public DetectedVideoType VideoType;
    public DetectedStereoLayout StereoLayout;
    public DetectedProjection Projection;

    public bool IsStereo;
    public float Confidence; // 0..1
    public bool IsGuess;

    public List<string> Evidence = new List<string>();

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"OriginalFileName: {OriginalFileName}");
        sb.AppendLine($"FileNameWithoutExtension: {FileNameWithoutExtension}");
        sb.AppendLine($"VideoType: {VideoType}");
        sb.AppendLine($"StereoLayout: {StereoLayout}");
        sb.AppendLine($"Projection: {Projection}");
        sb.AppendLine($"IsStereo: {IsStereo}");
        sb.AppendLine($"Confidence: {Confidence:0.00}");
        sb.AppendLine($"IsGuess: {IsGuess}");
        sb.AppendLine("Evidence: " + string.Join(", ", Evidence));
        return sb.ToString();
    }
}

public static class FilenameVideoTypeDetector
{
    // Delimiter-aware token boundary.
    // Similar idea to Kodi's 3D filename detection: only treat tokens as real tags
    // when separated by hyphen/space/dot/underscore/brackets, etc.
    private const string BoundaryStart = @"(?:^|[ \._\-\(\)\[\]\{\}])";
    private const string BoundaryEnd   = @"(?:$|[ \._\-\(\)\[\]\{\}])";

    public static FilenameVideoDetectionResult Detect(string pathOrFileName)
    {
        var result = new FilenameVideoDetectionResult
        {
            OriginalFileName = pathOrFileName ?? string.Empty
        };

        string name = Path.GetFileName(pathOrFileName ?? string.Empty);
        string stem = Path.GetFileNameWithoutExtension(name ?? string.Empty);

        result.FileNameWithoutExtension = stem;

        if (string.IsNullOrWhiteSpace(stem))
        {
            result.VideoType = DetectedVideoType.Unknown;
            result.StereoLayout = DetectedStereoLayout.Unknown;
            result.Projection = DetectedProjection.Unknown;
            result.IsStereo = false;
            result.Confidence = 0f;
            result.IsGuess = true;
            return result;
        }

        string lower = stem.ToLowerInvariant();
        string normalized = NormalizeForBoundaryMatching(lower);
        string compact = Regex.Replace(lower, @"[^a-z0-9]+", string.Empty);

        bool hasAnyTag = false;

        bool tag2D = HasToken(normalized, "2d");
        bool tag3D = HasToken(normalized, "3d");

        // Stereo tokens
        bool hasRL = HasToken(normalized, "rl") || compact.Contains("rightleft");
        bool hasLR = HasToken(normalized, "lr") || compact.Contains("leftright");

        bool hasSBS =
            HasToken(normalized, "sbs") ||
            HasToken(normalized, "hsbs") ||
            HasToken(normalized, "3dh") ||
            compact.Contains("sidebyside") ||
            compact.Contains("halfsbs") ||
            compact.Contains("halfsidebyside");

        bool hasTB =
            HasToken(normalized, "tb") ||
            HasToken(normalized, "tab") ||
            HasToken(normalized, "htab") ||
            HasToken(normalized, "ou") ||
            HasToken(normalized, "hou") ||
            HasToken(normalized, "3dv") ||
            compact.Contains("topbottom") ||
            compact.Contains("overunder") ||
            compact.Contains("halfou") ||
            compact.Contains("halfoverunder");

        bool hasBT =
            HasToken(normalized, "bt") ||
            compact.Contains("bottomtop");

        // Projection / FOV tokens
        bool hasVR180 =
            HasToken(normalized, "vr180") ||
            HasToken(normalized, "180x180") ||
            HasToken(normalized, "180");

        bool hasVR360 =
            HasToken(normalized, "vr360") ||
            HasToken(normalized, "360x180") ||
            HasToken(normalized, "360");

        bool hasF180 =
            HasToken(normalized, "f180") ||
            HasToken(normalized, "180f");

        bool hasFisheye190 =
            HasToken(normalized, "fisheye190") ||
            HasToken(normalized, "f190") ||
            HasToken(normalized, "190f") ||
            HasToken(normalized, "vr190") ||
            HasToken(normalized, "rf52");

        bool hasMKX200 =
            HasToken(normalized, "mkx200") ||
            HasToken(normalized, "fisheye200") ||
            HasToken(normalized, "f200") ||
            HasToken(normalized, "200f") ||
            HasToken(normalized, "vr200");

        bool hasMKX22 =
            HasToken(normalized, "mkx22") ||
            HasToken(normalized, "fisheye220") ||
            HasToken(normalized, "f220") ||
            HasToken(normalized, "220f") ||
            HasToken(normalized, "vr220");

        bool hasVRCA220 = HasToken(normalized, "vrca220");

        bool hasEAC360 =
            HasToken(normalized, "eac360") ||
            HasToken(normalized, "360eac");

        // ----- Stereo layout detection -----
        if (tag2D)
        {
            result.StereoLayout = DetectedStereoLayout.Mono;
            result.Evidence.Add("2D token");
            hasAnyTag = true;
        }
        else if (hasRL)
        {
            result.StereoLayout = DetectedStereoLayout.RightLeft;
            result.Evidence.Add("RL / right-left token");
            hasAnyTag = true;
        }
        else if (hasLR || hasSBS)
        {
            result.StereoLayout = DetectedStereoLayout.LeftRight;
            result.Evidence.Add(hasLR ? "LR / left-right token" : "SBS token");
            hasAnyTag = true;
        }
        else if (hasBT)
        {
            result.StereoLayout = DetectedStereoLayout.BottomTop;
            result.Evidence.Add("BT / bottom-top token");
            hasAnyTag = true;
        }
        else if (hasTB)
        {
            result.StereoLayout = DetectedStereoLayout.TopBottom;
            result.Evidence.Add("TB / TAB / OU token");
            hasAnyTag = true;
        }
        else if (tag3D)
        {
            // HereSphere treats _3D as left-right.
            // We keep that behavior, but with lower confidence because "3D" alone is ambiguous.
            result.StereoLayout = DetectedStereoLayout.LeftRight;
            result.Evidence.Add("3D token only -> guessed as LeftRight");
            hasAnyTag = true;
        }
        else
        {
            result.StereoLayout = DetectedStereoLayout.Unknown;
        }

        // ----- Projection detection -----
        if (hasEAC360)
        {
            result.Projection = DetectedProjection.EAC360;
            result.Evidence.Add("EAC360 token");
            hasAnyTag = true;
        }
        else if (hasF180)
        {
            result.Projection = DetectedProjection.Fisheye180;
            result.Evidence.Add("F180 / 180F token");
            hasAnyTag = true;
        }
        else if (hasFisheye190)
        {
            result.Projection = DetectedProjection.Fisheye190;
            result.Evidence.Add("FISHEYE190 / F190 / VR190 / RF52 token");
            hasAnyTag = true;
        }
        else if (hasMKX200)
        {
            result.Projection = DetectedProjection.Fisheye200;
            result.Evidence.Add("FISHEYE200 / F200 / VR200 / MKX200 token");
            hasAnyTag = true;
        }
        else if (hasMKX22 || hasVRCA220)
        {
            result.Projection = DetectedProjection.Fisheye220;
            result.Evidence.Add(hasMKX22 ? "FISHEYE220 / F220 / VR220 / MKX22 token" : "VRCA220 token");
            hasAnyTag = true;
        }
        else if (HasStrongVR180(normalized, compact))
        {
            result.Projection = DetectedProjection.VR180;
            result.Evidence.Add("VR180 / 180 token");
            hasAnyTag = true;
        }
        else if (HasStrongVR360(normalized, compact))
        {
            result.Projection = DetectedProjection.VR360;
            result.Evidence.Add("VR360 / 360 token");
            hasAnyTag = true;
        }
        else
        {
            result.Projection = DetectedProjection.Unknown;
        }

        // ----- Defaults -----
        // Common player behavior:
        // - 180/360 token without stereo token => mono immersive
        // - stereo token without 180/360/fisheye/eac => flat 3D
        if (result.Projection != DetectedProjection.Unknown &&
            result.StereoLayout == DetectedStereoLayout.Unknown)
        {
            result.StereoLayout = DetectedStereoLayout.Mono;
            result.Evidence.Add("No stereo tag, projection tag present -> default mono");
        }

        if (result.Projection == DetectedProjection.Unknown &&
            result.StereoLayout != DetectedStereoLayout.Unknown)
        {
            result.Projection = DetectedProjection.Flat;
            result.Evidence.Add("Stereo tag without VR projection tag -> default flat");
        }

        if (result.Projection == DetectedProjection.Unknown &&
            result.StereoLayout == DetectedStereoLayout.Unknown)
        {
            // Filename gave us nothing useful.
            // You can change this to Unknown if you prefer.
            result.Projection = DetectedProjection.Flat;
            result.StereoLayout = DetectedStereoLayout.Mono;
            result.Evidence.Add("No known tags -> guessed flat mono");
        }

        result.IsStereo = result.StereoLayout != DetectedStereoLayout.Mono &&
                          result.StereoLayout != DetectedStereoLayout.Unknown;

        result.VideoType = BuildVideoType(result.Projection, result.StereoLayout);

        result.Confidence = CalculateConfidence(
            tag2D: tag2D,
            tag3D: tag3D,
            stereo: result.StereoLayout,
            projection: result.Projection,
            evidenceCount: result.Evidence.Count,
            hadRecognizedTag: hasAnyTag);

        result.IsGuess = result.Confidence < 0.75f;

        return result;
    }

    private static string NormalizeForBoundaryMatching(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        string s = input.ToLowerInvariant();
        s = Regex.Replace(s, @"[+/\\]+", " ");
        s = Regex.Replace(s, @"[\(\)\[\]\{\}]+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static bool HasToken(string normalized, string token)
    {
        string pattern = BoundaryStart + Regex.Escape(token.ToLowerInvariant()) + BoundaryEnd;
        return Regex.IsMatch(normalized, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasStrongVR180(string normalized, string compact)
    {
        // Keep exact 180 token, plus stronger forms like vr180 / 180x180.
        return HasToken(normalized, "vr180") ||
               HasToken(normalized, "180x180") ||
               HasToken(normalized, "180");
    }

    private static bool HasStrongVR360(string normalized, string compact)
    {
        // Keep exact 360 token, plus stronger forms like vr360 / 360x180.
        return HasToken(normalized, "vr360") ||
               HasToken(normalized, "360x180") ||
               HasToken(normalized, "360");
    }

    private static DetectedVideoType BuildVideoType(DetectedProjection projection, DetectedStereoLayout stereo)
    {
        bool isStereo =
            stereo == DetectedStereoLayout.LeftRight ||
            stereo == DetectedStereoLayout.RightLeft ||
            stereo == DetectedStereoLayout.TopBottom ||
            stereo == DetectedStereoLayout.BottomTop;

        switch (projection)
        {
            case DetectedProjection.Flat:
                return isStereo ? DetectedVideoType.Flat3D : DetectedVideoType.Flat2D;

            case DetectedProjection.VR180:
                return isStereo ? DetectedVideoType.VR1803D : DetectedVideoType.VR1802D;

            case DetectedProjection.VR360:
                return isStereo ? DetectedVideoType.VR3603D : DetectedVideoType.VR3602D;

            case DetectedProjection.Fisheye180:
                return isStereo ? DetectedVideoType.Fisheye1803D : DetectedVideoType.Fisheye1802D;

            case DetectedProjection.Fisheye190:
                return isStereo ? DetectedVideoType.Fisheye1903D : DetectedVideoType.Fisheye1902D;

            case DetectedProjection.Fisheye200:
                return isStereo ? DetectedVideoType.Fisheye2003D : DetectedVideoType.Fisheye2002D;

            case DetectedProjection.Fisheye220:
                return isStereo ? DetectedVideoType.Fisheye2203D : DetectedVideoType.Fisheye2202D;

            case DetectedProjection.EAC360:
                return isStereo ? DetectedVideoType.EAC3603D : DetectedVideoType.EAC3602D;

            default:
                return DetectedVideoType.Unknown;
        }
    }

    private static float CalculateConfidence(
        bool tag2D,
        bool tag3D,
        DetectedStereoLayout stereo,
        DetectedProjection projection,
        int evidenceCount,
        bool hadRecognizedTag)
    {
        if (!hadRecognizedTag)
            return 0.20f; // pure guess: "looks like flat mono because filename had no useful tags"

        float c = 0.30f;

        if (stereo != DetectedStereoLayout.Unknown)
            c += 0.20f;

        if (projection != DetectedProjection.Unknown)
            c += 0.20f;

        if (tag2D)
            c += 0.10f;

        if (tag3D)
            c += 0.05f;

        bool stereoExplicit =
            stereo == DetectedStereoLayout.LeftRight ||
            stereo == DetectedStereoLayout.RightLeft ||
            stereo == DetectedStereoLayout.TopBottom ||
            stereo == DetectedStereoLayout.BottomTop;

        bool immersive =
            projection == DetectedProjection.VR180 ||
            projection == DetectedProjection.VR360 ||
            projection == DetectedProjection.Fisheye180 ||
            projection == DetectedProjection.Fisheye190 ||
            projection == DetectedProjection.Fisheye200 ||
            projection == DetectedProjection.Fisheye220 ||
            projection == DetectedProjection.EAC360;

        if (stereoExplicit && immersive)
            c += 0.10f;

        if (evidenceCount >= 2)
            c += 0.05f;

        return Mathf.Clamp01(c);
    }
}
