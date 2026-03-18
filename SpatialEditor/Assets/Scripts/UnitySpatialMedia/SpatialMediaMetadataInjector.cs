using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEngine;
using UnitySpatialMedia.Internal;

namespace UnitySpatialMedia
{
    public static class SpatialMediaMetadataInjector
    {
        private static readonly string[] SupportedExtensions = { ".mp4", ".mov" };
        private static readonly byte[] SphericalUuidId =
        {
            0xff, 0xcc, 0x82, 0x63, 0xf8, 0x55, 0x4a, 0x93,
            0x88, 0x14, 0x58, 0x7a, 0x02, 0x52, 0x1f, 0xdd
        };
        private static readonly Regex CropRegex =
            new Regex(@"^(\d+):(\d+):(\d+):(\d+):(\d+):(\d+)$", RegexOptions.Compiled);

        private const string SphericalXmlHeader =
            "<?xml version=\"1.0\"?>" +
            "<rdf:SphericalVideo\n" +
            "xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"\n" +
            "xmlns:GSpherical=\"http://ns.google.com/videos/1.0/spherical/\">";
        private const string SphericalXmlContents =
            "<GSpherical:Spherical>true</GSpherical:Spherical>" +
            "<GSpherical:Stitched>true</GSpherical:Stitched>" +
            "<GSpherical:StitchingSoftware>Spherical Metadata Tool</GSpherical:StitchingSoftware>" +
            "<GSpherical:ProjectionType>equirectangular</GSpherical:ProjectionType>";
        private const string NotSphericalXmlContents =
            "<GSpherical:Spherical>false</GSpherical:Spherical>" +
            "<GSpherical:Stitched>false</GSpherical:Stitched>" +
            "<GSpherical:StitchingSoftware>Spherical Metadata Tool</GSpherical:StitchingSoftware>" +
            "<GSpherical:ProjectionType>rectangular</GSpherical:ProjectionType>";
        private const string SphericalXmlContentsTopBottom =
            "<GSpherical:StereoMode>top-bottom</GSpherical:StereoMode>";
        private const string SphericalXmlContentsLeftRight =
            "<GSpherical:StereoMode>left-right</GSpherical:StereoMode>";
        private const string SphericalXmlContentsCropFormat =
            "<GSpherical:CroppedAreaImageWidthPixels>{0}</GSpherical:CroppedAreaImageWidthPixels>" +
            "<GSpherical:CroppedAreaImageHeightPixels>{1}</GSpherical:CroppedAreaImageHeightPixels>" +
            "<GSpherical:FullPanoWidthPixels>{2}</GSpherical:FullPanoWidthPixels>" +
            "<GSpherical:FullPanoHeightPixels>{3}</GSpherical:FullPanoHeightPixels>" +
            "<GSpherical:CroppedAreaLeftPixels>{4}</GSpherical:CroppedAreaLeftPixels>" +
            "<GSpherical:CroppedAreaTopPixels>{5}</GSpherical:CroppedAreaTopPixels>";
        private const string SphericalXmlFooter = "</rdf:SphericalVideo>";
        public const string DefaultVr180Bounds = "0:0:0x40000000:0x40000000";

        public enum StereoMode : byte
        {
            Mono = 0,
            TopBottom = 1,
            LeftRight = 2,
            StereoCustom = 3,
            RightLeft = 4
        }

        public enum StereoLayout
        {
            None,
            Mono,
            TopBottom,
            LeftRight,
            StereoCustom,
            RightLeft
        }

        public enum ProjectionMode
        {
            None,
            Equirectangular
        }

        public enum VideoPreset
        {
            Vr360Mono,
            Vr360LeftRight,
            Vr360TopBottom,
            Vr180LeftRight,
            Vr180RightLeft,
            Flat3dLeftRight,
            Flat3dTopBottom
        }

        public sealed class Metadata
        {
            public string Projection;
            public string StereoMode;
            public uint[] Bounds;
            public string VideoXml;
            public SpatialAudioMetadata Audio;

            public Metadata(string projection = "equirectangular", string stereoMode = "none", string bounds = null)
            {
                Projection = string.IsNullOrEmpty(projection) || projection == "none" ? null : projection;
                StereoMode = string.IsNullOrEmpty(stereoMode) || stereoMode == "none" ? null : stereoMode;
                Bounds = ParseBounds(bounds);
            }

            public Metadata(ProjectionMode projection, StereoLayout stereoMode, string bounds = null)
                : this(ToProjectionString(projection), ToStereoLayoutString(stereoMode), bounds)
            {
            }
        }

        public sealed class SpatialAudioMetadata
        {
            public string AmbisonicType = "periphonic";
            public int AmbisonicOrder = 1;
            public bool HeadLockedStereo;
            public string AmbisonicChannelOrdering = "ACN";
            public string AmbisonicNormalization = "SN3D";
            public List<int> ChannelMap = new List<int>();
        }

        public sealed class ParsedMetadata
        {
            public readonly Dictionary<string, Dictionary<string, string>> VideoV1 =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            public readonly List<VideoV2Metadata> VideoV2 = new List<VideoV2Metadata>();
            public SpatialAudioMetadata Audio;
            public int NumAudioChannels;
        }

        public sealed class VideoV2Metadata
        {
            public string TrackName;
            public StereoMode? Stereo;
            public uint PoseYaw;
            public uint PosePitch;
            public uint PoseRoll;
            public uint BoundsTop;
            public uint BoundsBottom;
            public uint BoundsLeft;
            public uint BoundsRight;
        }

        public readonly struct SpatialAudioDescription
        {
            public readonly int Order;
            public readonly bool IsSupported;
            public readonly bool HasHeadLockedStereo;

            public SpatialAudioDescription(int order, bool isSupported, bool hasHeadLockedStereo)
            {
                Order = order;
                IsSupported = isSupported;
                HasHeadLockedStereo = hasHeadLockedStereo;
            }
        }

        public static SpatialAudioDescription GetSpatialAudioDescription(int numChannels)
        {
            const int maxOrder = 1;
            for (int i = 1; i <= maxOrder; i++)
            {
                if ((i + 1) * (i + 1) == numChannels)
                {
                    return new SpatialAudioDescription(i, true, false);
                }

                if (((i + 1) * (i + 1) + 2) == numChannels)
                {
                    return new SpatialAudioDescription(i, true, true);
                }
            }

            return new SpatialAudioDescription(-1, false, true);
        }

        public static SpatialAudioMetadata GetSpatialAudioMetadata(int ambisonicOrder, bool headLockedStereo)
        {
            int channels = GetExpectedNumAudioChannels("periphonic", ambisonicOrder, headLockedStereo);
            return new SpatialAudioMetadata
            {
                AmbisonicOrder = ambisonicOrder,
                HeadLockedStereo = headLockedStereo,
                ChannelMap = Enumerable.Range(0, channels).ToList()
            };
        }

        public static string GenerateSphericalXml(
            string projection = "equirectangular",
            string stereo = null,
            string crop = null,
            Action<string> console = null)
        {
            Action<string> log = ResolveLogger(console);
            string additionalXml = string.Empty;
            if (stereo == "top-bottom") additionalXml += SphericalXmlContentsTopBottom;
            else if (stereo == "left-right") additionalXml += SphericalXmlContentsLeftRight;

            if (!string.IsNullOrEmpty(crop))
            {
                Match m = CropRegex.Match(crop);
                if (!m.Success)
                {
                    log($"Error: Invalid crop params: {crop}");
                    return null;
                }

                int cw = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int ch = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int fw = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                int fh = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                int ox = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
                int oy = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);

                if (fw <= 0 || fh <= 0 || cw <= 0 || ch <= 0 || cw > fw || ch > fh)
                {
                    log("Error with crop params.");
                    return null;
                }

                if (ox < 0 || oy < 0 || ox + cw > fw || oy + ch > fh)
                {
                    log("Error with crop offsets.");
                    return null;
                }

                additionalXml += string.Format(
                    CultureInfo.InvariantCulture,
                    SphericalXmlContentsCropFormat,
                    cw, ch, fw, fh, ox, oy);
            }

            return SphericalXmlHeader +
                   (projection == "equirectangular" ? SphericalXmlContents : NotSphericalXmlContents) +
                   additionalXml +
                   SphericalXmlFooter;
        }

        public static string GenerateSphericalXml(
            ProjectionMode projection,
            StereoLayout stereo = StereoLayout.None,
            string crop = null,
            Action<string> console = null)
        {
            return GenerateSphericalXml(
                ToProjectionString(projection),
                ToStereoLayoutString(stereo),
                crop,
                console);
        }

        public static ParsedMetadata ParseMetadata(string src, Action<string> console = null)
        {
            Action<string> log = ResolveLogger(console);
            string input = Path.GetFullPath(src);
            if (!File.Exists(input))
            {
                log($"Error: {input} does not exist.");
                return null;
            }

            if (!SupportedExtensions.Contains(Path.GetExtension(input).ToLowerInvariant(), StringComparer.Ordinal))
            {
                log("Unknown file type.");
                return null;
            }

            using (var inFs = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Mp4File mp4 = Mp4Parser.Load(inFs, log);
                if (mp4 == null) return null;
                return ParseSphericalMpeg4(mp4, inFs, log);
            }
        }

        public static bool InjectMetadata(string src, string dest, Metadata metadata, Action<string> console = null)
        {
            Action<string> log = ResolveLogger(console);
            string input = Path.GetFullPath(src);
            string output = Path.GetFullPath(dest);
            if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            {
                log("Input and output cannot be the same.");
                return false;
            }

            if (!File.Exists(input))
            {
                log($"Error: {input} does not exist.");
                return false;
            }

            if (!SupportedExtensions.Contains(Path.GetExtension(input).ToLowerInvariant(), StringComparer.Ordinal))
            {
                log("Unknown file type.");
                return false;
            }

            try
            {
                using (var inFs = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Mp4File mp4 = Mp4Parser.Load(inFs, log);
                    if (mp4 == null) return false;

                    if (!string.IsNullOrEmpty(metadata.VideoXml) && !Mpeg4AddSphericalXmlV1(mp4, inFs, metadata.VideoXml))
                    {
                        log("Error failed to insert spherical data.");
                        return false;
                    }

                    if ((!string.IsNullOrEmpty(metadata.Projection) || !string.IsNullOrEmpty(metadata.StereoMode)) &&
                        !Mpeg4AddSphericalV2(mp4, inFs, metadata.Projection, metadata.StereoMode, metadata.Bounds))
                    {
                        log("Error failed to insert spherical data v2.");
                        return false;
                    }

                    if (metadata.Audio != null && !Mpeg4AddAudioMetadata(mp4, inFs, metadata.Audio, log))
                    {
                        log("Error failed to insert spatial audio data.");
                        return false;
                    }

                    using (var outFs = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        mp4.Save(inFs, outFs);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                log($"Exception: {ex.Message}");
                if (File.Exists(output)) File.Delete(output);
                return false;
            }
        }

        public static bool InjectV2(
            string inputPath,
            string outputPath,
            string stereoMode = "left-right",
            string projection = "equirectangular",
            string bounds = null,
            Action<string> console = null)
        {
            var metadata = new Metadata(projection, stereoMode, bounds);
            return InjectMetadata(inputPath, outputPath, metadata, console);
        }

        public static bool InjectV2(
            string inputPath,
            string outputPath,
            StereoLayout stereoMode = StereoLayout.LeftRight,
            ProjectionMode projection = ProjectionMode.Equirectangular,
            string bounds = null,
            Action<string> console = null)
        {
            var metadata = new Metadata(
                ToProjectionString(projection),
                ToStereoLayoutString(stereoMode),
                bounds);
            return InjectMetadata(inputPath, outputPath, metadata, console);
        }

        public static Metadata CreatePresetMetadata(
            VideoPreset preset,
            bool includeV1Xml = false,
            string vr180Bounds = null,
            string crop = null,
            Action<string> console = null)
        {
            Action<string> log = ResolveLogger(console);
            string stereo;
            string projection;
            string bounds;

            switch (preset)
            {
                case VideoPreset.Vr360Mono:
                    stereo = "mono";
                    projection = "equirectangular";
                    bounds = "0:0:0:0";
                    break;
                case VideoPreset.Vr360LeftRight:
                    stereo = "left-right";
                    projection = "equirectangular";
                    bounds = "0:0:0:0";
                    break;
                case VideoPreset.Vr360TopBottom:
                    stereo = "top-bottom";
                    projection = "equirectangular";
                    bounds = "0:0:0:0";
                    break;
                case VideoPreset.Vr180LeftRight:
                    stereo = "left-right";
                    projection = "equirectangular";
                    bounds = string.IsNullOrWhiteSpace(vr180Bounds) ? DefaultVr180Bounds : vr180Bounds;
                    break;
                case VideoPreset.Vr180RightLeft:
                    stereo = "right-left";
                    projection = "equirectangular";
                    bounds = string.IsNullOrWhiteSpace(vr180Bounds) ? DefaultVr180Bounds : vr180Bounds;
                    break;
                case VideoPreset.Flat3dLeftRight:
                    stereo = "left-right";
                    projection = "none";
                    bounds = null;
                    break;
                case VideoPreset.Flat3dTopBottom:
                    stereo = "top-bottom";
                    projection = "none";
                    bounds = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }

            var metadata = new Metadata(projection, stereo, bounds);
            if (includeV1Xml)
            {
                metadata.VideoXml = GenerateSphericalXml(projection, stereo, crop, log);
            }

            return metadata;
        }

        public static bool InjectPreset(
            string inputPath,
            string outputPath,
            VideoPreset preset,
            bool includeV1Xml = false,
            string vr180Bounds = null,
            string crop = null,
            Action<string> console = null)
        {
            Metadata metadata = CreatePresetMetadata(preset, includeV1Xml, vr180Bounds, crop, console);
            return InjectMetadata(inputPath, outputPath, metadata, console);
        }

        public static bool InjectStereoOnly(
            string inputPath,
            string outputPath,
            StereoMode stereoMode = StereoMode.LeftRight,
            Action<string> console = null)
        {
            var metadata = new Metadata("none", ToStereoString(stereoMode), null)
            {
                Projection = null
            };
            return InjectMetadata(inputPath, outputPath, metadata, console);
        }

        private static Action<string> ResolveLogger(Action<string> console)
        {
            return console ?? (msg => Debug.Log("[SpatialMediaMetadataInjector] " + msg));
        }

        private static string ToStereoString(StereoMode mode)
        {
            switch (mode)
            {
                case StereoMode.Mono: return "mono";
                case StereoMode.TopBottom: return "top-bottom";
                case StereoMode.LeftRight: return "left-right";
                case StereoMode.RightLeft: return "right-left";
                default: return "none";
            }
        }

        private static string ToStereoLayoutString(StereoLayout mode)
        {
            switch (mode)
            {
                case StereoLayout.None: return "none";
                case StereoLayout.Mono: return "mono";
                case StereoLayout.TopBottom: return "top-bottom";
                case StereoLayout.LeftRight: return "left-right";
                case StereoLayout.StereoCustom: return "stereo-custom";
                case StereoLayout.RightLeft: return "right-left";
                default: return "none";
            }
        }

        private static string ToProjectionString(ProjectionMode mode)
        {
            switch (mode)
            {
                case ProjectionMode.None: return "none";
                case ProjectionMode.Equirectangular: return "equirectangular";
                default: return "none";
            }
        }

        private static bool Mpeg4AddSphericalXmlV1(Mp4File mp4, Stream input, string xml)
        {
            foreach (Mp4Box child in mp4.MoovBox.Children)
            {
                if (child.Name != Mp4Tags.TRAK || !(child is ContainerMp4Box track)) continue;
                track.RemoveRecursive(Mp4Tags.UUID);

                bool isVideoTrack = false;
                foreach (Mp4Box trackChild in track.Children)
                {
                    if (trackChild.Name != Mp4Tags.MDIA || !(trackChild is ContainerMp4Box mdia)) continue;
                    foreach (Mp4Box mdiaChild in mdia.Children)
                    {
                        if (mdiaChild.Name != Mp4Tags.HDLR) continue;
                        input.Position = mdiaChild.ContentStart + 8;
                        if (BigEndian.ReadTag(input) == Mp4Tags.VIDE)
                        {
                            isVideoTrack = true;
                            break;
                        }
                    }
                    if (isVideoTrack)
                    {
                        byte[] xmlBytes = Encoding.UTF8.GetBytes(xml);
                        byte[] payload = new byte[SphericalUuidId.Length + xmlBytes.Length];
                        Buffer.BlockCopy(SphericalUuidId, 0, payload, 0, SphericalUuidId.Length);
                        Buffer.BlockCopy(xmlBytes, 0, payload, SphericalUuidId.Length, xmlBytes.Length);
                        if (!track.Add(new RawMp4Box(Mp4Tags.UUID, 0, 8, payload.Length, payload)))
                        {
                            return false;
                        }
                        break;
                    }
                }
            }

            mp4.Resize();
            return true;
        }

        private static bool Mpeg4AddSphericalV2(Mp4File mp4, Stream input, string projection, string stereoMode, uint[] bounds)
        {
            foreach (Mp4Box child in mp4.MoovBox.Children)
            {
                if (child.Name != Mp4Tags.TRAK || !(child is ContainerMp4Box track)) continue;
                foreach (Mp4Box trackChild in track.Children)
                {
                    if (trackChild.Name != Mp4Tags.MDIA || !(trackChild is ContainerMp4Box mdia)) continue;
                    foreach (Mp4Box mdiaChild in mdia.Children)
                    {
                        if (mdiaChild.Name != Mp4Tags.HDLR) continue;
                        input.Position = mdiaChild.ContentStart + 8;
                        if (BigEndian.ReadTag(input) == Mp4Tags.VIDE)
                        {
                            bool ok = InjectSpatialVideoV2Atoms(mdia, projection, stereoMode, bounds);
                            mp4.Resize();
                            return ok;
                        }
                    }
                }
            }

            return false;
        }

        private static bool InjectSpatialVideoV2Atoms(ContainerMp4Box mdia, string projection, string stereoMode, uint[] bounds)
        {
            foreach (Mp4Box atom in mdia.Children)
            {
                if (atom.Name != Mp4Tags.MINF || !(atom is ContainerMp4Box minf)) continue;
                foreach (Mp4Box minfChild in minf.Children)
                {
                    if (minfChild.Name != Mp4Tags.STBL || !(minfChild is ContainerMp4Box stbl)) continue;
                    foreach (Mp4Box stblChild in stbl.Children)
                    {
                        if (stblChild.Name != Mp4Tags.STSD || !(stblChild is ContainerMp4Box stsd)) continue;
                        foreach (Mp4Box descBox in stsd.Children)
                        {
                            if (!(descBox is ContainerMp4Box desc) || !Mp4Tags.VideoSampleDescriptions.Contains(desc.Name))
                            {
                                continue;
                            }

                            if (!string.IsNullOrEmpty(stereoMode))
                            {
                                var st3d = new St3dMp4Box();
                                if (!st3d.SetStereoMode(stereoMode))
                                {
                                    return false;
                                }
                                desc.RemoveByName(Mp4Tags.ST3D);
                                desc.Add(st3d);
                            }

                            if (!string.IsNullOrEmpty(projection))
                            {
                                var proj = new ContainerMp4Box(Mp4Tags.PROJ, 0, 8, 0, 0);
                                proj.Add(new PrhdMp4Box());
                                proj.Add(new EquiMp4Box
                                {
                                    Top = bounds != null && bounds.Length > 0 ? bounds[0] : 0,
                                    Bottom = bounds != null && bounds.Length > 1 ? bounds[1] : 0,
                                    Left = bounds != null && bounds.Length > 2 ? bounds[2] : 0,
                                    Right = bounds != null && bounds.Length > 3 ? bounds[3] : 0
                                });
                                var sv3d = new ContainerMp4Box(Mp4Tags.SV3D, 0, 8, 0, 0);
                                sv3d.Add(proj);
                                desc.RemoveByName(Mp4Tags.SV3D);
                                desc.Add(sv3d);
                            }
                        }
                    }
                }
            }

            return true;
        }

        private static int GetExpectedNumAudioChannels(string ambisonicsType, int order, bool headLockedStereo)
        {
            if (ambisonicsType != "periphonic") return -1;
            return (order + 1) * (order + 1) + (headLockedStereo ? 2 : 0);
        }

        private static bool Mpeg4AddAudioMetadata(Mp4File mp4, Stream input, SpatialAudioMetadata audio, Action<string> log)
        {
            int tracks = GetNumAudioTracks(mp4, input);
            if (tracks > 1)
            {
                log($"Error: Expected 1 audio track. Found {tracks}");
                return false;
            }
            return Mpeg4AddSpatialAudio(mp4, input, audio, log);
        }

        private static bool Mpeg4AddSpatialAudio(Mp4File mp4, Stream input, SpatialAudioMetadata audio, Action<string> log)
        {
            foreach (Mp4Box child in mp4.MoovBox.Children)
            {
                if (child.Name != Mp4Tags.TRAK || !(child is ContainerMp4Box track)) continue;
                foreach (Mp4Box trackChild in track.Children)
                {
                    if (trackChild.Name != Mp4Tags.MDIA || !(trackChild is ContainerMp4Box mdia)) continue;
                    foreach (Mp4Box mdiaChild in mdia.Children)
                    {
                        if (mdiaChild.Name != Mp4Tags.HDLR) continue;
                        input.Position = mdiaChild.ContentStart + 8;
                        if (BigEndian.ReadTag(input) == Mp4Tags.SOUN)
                        {
                            return InjectSpatialAudioAtom(input, mdia, audio, log);
                        }
                    }
                }
            }
            return true;
        }

        private static bool InjectSpatialAudioAtom(Stream input, ContainerMp4Box mdia, SpatialAudioMetadata audio, Action<string> log)
        {
            foreach (Mp4Box atom in mdia.Children)
            {
                if (atom.Name != Mp4Tags.MINF || !(atom is ContainerMp4Box minf)) continue;
                foreach (Mp4Box minfChild in minf.Children)
                {
                    if (minfChild.Name != Mp4Tags.STBL || !(minfChild is ContainerMp4Box stbl)) continue;
                    foreach (Mp4Box stblChild in stbl.Children)
                    {
                        if (stblChild.Name != Mp4Tags.STSD || !(stblChild is ContainerMp4Box stsd)) continue;
                        foreach (Mp4Box descBox in stsd.Children)
                        {
                            if (!(descBox is ContainerMp4Box desc) || !Mp4Tags.SoundSampleDescriptions.Contains(desc.Name))
                            {
                                continue;
                            }

                            int channels = GetNumAudioChannels(stsd, input);
                            int expected = GetExpectedNumAudioChannels(audio.AmbisonicType, audio.AmbisonicOrder, audio.HeadLockedStereo);
                            if (channels != expected)
                            {
                                log($"Error: Found {channels} channel(s). Expected {expected}.");
                                return false;
                            }

                            var sa3d = new Sa3dMp4Box
                            {
                                Version = 0,
                                AmbisonicType = 0,
                                HeadLockedStereo = audio.HeadLockedStereo,
                                AmbisonicOrder = (uint)audio.AmbisonicOrder,
                                ChannelOrdering = 0,
                                Normalization = 0,
                                NumChannels = (uint)channels
                            };

                            foreach (int channel in audio.ChannelMap)
                            {
                                sa3d.ChannelMap.Add((uint)channel);
                            }
                            sa3d.ContentSize = 1 + 1 + 4 + 1 + 1 + 4 + (4 * sa3d.ChannelMap.Count);
                            desc.Children.Add(sa3d);
                        }
                    }
                }
            }

            return true;
        }

        private static int GetNumAudioTracks(Mp4File mp4, Stream input)
        {
            int tracks = 0;
            foreach (Mp4Box child in mp4.MoovBox.Children)
            {
                if (child.Name != Mp4Tags.TRAK || !(child is ContainerMp4Box track)) continue;
                foreach (Mp4Box trackChild in track.Children)
                {
                    if (trackChild.Name != Mp4Tags.MDIA || !(trackChild is ContainerMp4Box mdia)) continue;
                    foreach (Mp4Box mdiaChild in mdia.Children)
                    {
                        if (mdiaChild.Name != Mp4Tags.HDLR) continue;
                        input.Position = mdiaChild.ContentStart + 8;
                        if (BigEndian.ReadTag(input) == Mp4Tags.SOUN) tracks++;
                    }
                }
            }
            return tracks;
        }

        private static int GetNumAudioChannels(ContainerMp4Box stsd, Stream input)
        {
            foreach (Mp4Box sample in stsd.Children)
            {
                if (sample.Name == Mp4Tags.MP4A) return GetAacNumChannels(sample, input);
                if (Mp4Tags.SoundSampleDescriptions.Contains(sample.Name)) return GetSampleDescriptionNumChannels(sample, input);
            }
            return -1;
        }

        private static int GetSampleDescriptionNumChannels(Mp4Box sampleDescription, Stream input)
        {
            long p = input.Position;
            input.Position = sampleDescription.ContentStart + 8;
            short version = BigEndian.ReadI16(input);
            _ = BigEndian.ReadI16(input);
            _ = BigEndian.ReadI32(input);
            int channels;
            if (version == 0)
            {
                channels = BigEndian.ReadI16(input);
                _ = BigEndian.ReadI16(input);
            }
            else if (version == 1)
            {
                channels = BigEndian.ReadI16(input);
                _ = BigEndian.ReadI16(input);
                _ = BigEndian.ReadI32(input);
                _ = BigEndian.ReadI32(input);
                _ = BigEndian.ReadI32(input);
                _ = BigEndian.ReadI32(input);
            }
            else if (version == 2)
            {
                _ = BigEndian.ReadI16(input);
                _ = BigEndian.ReadI16(input);
                _ = BigEndian.ReadI16(input);
                _ = BigEndian.ReadI16(input);
                _ = BigEndian.ReadI32(input);
                _ = BigEndian.ReadI32(input);
                _ = BigEndian.ReadDouble(input);
                channels = BigEndian.ReadI32(input);
            }
            else
            {
                input.Position = p;
                return -1;
            }

            input.Position = p;
            return channels;
        }

        private static int GetAacNumChannels(Mp4Box box, Stream input)
        {
            long p = input.Position;
            int channelConfiguration = -1;
            if (!(box is ContainerMp4Box container))
            {
                return -1;
            }

            foreach (Mp4Box element in container.Children)
            {
                if (element.Name == Mp4Tags.WAVE)
                {
                    channelConfiguration = GetAacNumChannels(element, input);
                    break;
                }

                if (element.Name != Mp4Tags.ESDS)
                {
                    continue;
                }

                input.Position = element.ContentStart + 4;
                int descriptorTag = input.ReadByte();
                if (descriptorTag != 3)
                {
                    input.Position = p;
                    return -1;
                }

                _ = GetDescriptorLength(input);
                input.Position += 3;

                int configDescriptorTag = input.ReadByte();
                if (configDescriptorTag != 4)
                {
                    input.Position = p;
                    return -1;
                }

                _ = GetDescriptorLength(input);
                input.Position += 13;

                int decoderSpecificTag = input.ReadByte();
                if (decoderSpecificTag != 5)
                {
                    input.Position = p;
                    return -1;
                }

                int audioSpecificSize = GetDescriptorLength(input);
                if (audioSpecificSize < 2)
                {
                    input.Position = p;
                    return -1;
                }

                ushort descriptor = BigEndian.ReadU16(input);
                int sfIndex = (descriptor & 0x0780) >> 7;
                if (sfIndex == 0)
                {
                    input.Position = p;
                    return -1;
                }

                channelConfiguration = (descriptor & 0x0078) >> 3;
            }

            input.Position = p;
            return channelConfiguration;
        }

        private static int GetDescriptorLength(Stream input)
        {
            int length = 0;
            for (int i = 0; i < 4; i++)
            {
                int b = input.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                length = (length << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            return length;
        }

        private static ParsedMetadata ParseSphericalMpeg4(Mp4File mp4, Stream input, Action<string> log)
        {
            var parsed = new ParsedMetadata();
            int trackIndex = 0;

            foreach (Mp4Box child in mp4.MoovBox.Children)
            {
                if (child.Name != Mp4Tags.TRAK || !(child is ContainerMp4Box track)) continue;
                string trackName = $"Track {trackIndex++}";
                var v2 = new VideoV2Metadata { TrackName = trackName };
                bool hasV2 = false;

                foreach (Mp4Box trackChild in track.Children)
                {
                    if (trackChild.Name == Mp4Tags.UUID)
                    {
                        byte[] content = trackChild.ReadContent(input);
                        if (content.Length > 16 && SphericalUuidId.SequenceEqual(content.Take(16)))
                        {
                            string xml = Encoding.UTF8.GetString(content, 16, content.Length - 16);
                            Dictionary<string, string> map = ParseSphericalXml(xml, log);
                            if (map != null) parsed.VideoV1[trackName] = map;
                        }
                    }

                    if (trackChild.Name != Mp4Tags.MDIA || !(trackChild is ContainerMp4Box mdia)) continue;
                    foreach (Mp4Box mdiaChild in mdia.Children)
                    {
                        if (mdiaChild.Name != Mp4Tags.MINF || !(mdiaChild is ContainerMp4Box minf)) continue;
                        foreach (Mp4Box minfChild in minf.Children)
                        {
                            if (minfChild.Name != Mp4Tags.STBL || !(minfChild is ContainerMp4Box stbl)) continue;
                            foreach (Mp4Box stblChild in stbl.Children)
                            {
                                if (stblChild.Name != Mp4Tags.STSD || !(stblChild is ContainerMp4Box stsd)) continue;
                                ParseStsdForMetadata(stsd, input, parsed, ref v2, ref hasV2);
                            }
                        }
                    }
                }

                if (hasV2) parsed.VideoV2.Add(v2);
            }

            return parsed;
        }

        private static void ParseStsdForMetadata(
            ContainerMp4Box stsd,
            Stream input,
            ParsedMetadata parsed,
            ref VideoV2Metadata v2,
            ref bool hasV2)
        {
            foreach (Mp4Box sampleDesc in stsd.Children)
            {
                if (Mp4Tags.SoundSampleDescriptions.Contains(sampleDesc.Name) && sampleDesc is ContainerMp4Box sound)
                {
                    parsed.NumAudioChannels = GetNumAudioChannels(stsd, input);
                    foreach (Mp4Box soundChild in sound.Children)
                    {
                        if (soundChild is Sa3dMp4Box sa3d)
                        {
                            parsed.Audio = new SpatialAudioMetadata
                            {
                                AmbisonicType = "periphonic",
                                AmbisonicOrder = (int)sa3d.AmbisonicOrder,
                                HeadLockedStereo = sa3d.HeadLockedStereo,
                                AmbisonicChannelOrdering = "ACN",
                                AmbisonicNormalization = "SN3D",
                                ChannelMap = sa3d.ChannelMap.Select(v => (int)v).ToList()
                            };
                        }
                    }
                }

                if (!Mp4Tags.VideoSampleDescriptions.Contains(sampleDesc.Name) || !(sampleDesc is ContainerMp4Box video))
                {
                    continue;
                }

                foreach (Mp4Box videoChild in video.Children)
                {
                    if (videoChild is St3dMp4Box st3d)
                    {
                        v2.Stereo = (StereoMode)st3d.Mode;
                        hasV2 = true;
                    }
                    else if (videoChild.Name == Mp4Tags.SV3D && videoChild is ContainerMp4Box sv3d)
                    {
                        ParseSv3dForMetadata(sv3d, ref v2, ref hasV2);
                    }
                }
            }
        }

        private static void ParseSv3dForMetadata(ContainerMp4Box sv3d, ref VideoV2Metadata v2, ref bool hasV2)
        {
            foreach (Mp4Box sv3dChild in sv3d.Children)
            {
                if (sv3dChild.Name != Mp4Tags.PROJ || !(sv3dChild is ContainerMp4Box proj)) continue;
                foreach (Mp4Box projChild in proj.Children)
                {
                    if (projChild is PrhdMp4Box prhd)
                    {
                        v2.PoseYaw = prhd.Yaw;
                        v2.PosePitch = prhd.Pitch;
                        v2.PoseRoll = prhd.Roll;
                        hasV2 = true;
                    }
                    else if (projChild is EquiMp4Box equi)
                    {
                        v2.BoundsTop = equi.Top;
                        v2.BoundsBottom = equi.Bottom;
                        v2.BoundsLeft = equi.Left;
                        v2.BoundsRight = equi.Right;
                        hasV2 = true;
                    }
                }
            }
        }

        private static Dictionary<string, string> ParseSphericalXml(string xml, Action<string> log)
        {
            try
            {
                XDocument doc = XDocument.Parse(xml);
                XElement root = doc.Root;
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                if (root == null) return map;
                foreach (XElement child in root.Elements())
                {
                    string key = child.Name.NamespaceName == "http://ns.google.com/videos/1.0/spherical/"
                        ? child.Name.LocalName
                        : child.Name.ToString();
                    map[key] = child.Value;
                    log($"\t\t{key} = {child.Value}");
                }
                return map;
            }
            catch
            {
                return null;
            }
        }

        private static uint[] ParseBounds(string bounds)
        {
            if (string.IsNullOrWhiteSpace(bounds)) return null;
            string[] split = bounds.Split(':');
            if (split.Length != 4) return null;
            var values = new uint[4];
            for (int i = 0; i < 4; i++)
            {
                string token = split[i];
                if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (!uint.TryParse(token.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out values[i]))
                    {
                        return null;
                    }
                }
                else
                {
                    if (!uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                    {
                        return null;
                    }
                }
            }
            return values;
        }
    }
}
