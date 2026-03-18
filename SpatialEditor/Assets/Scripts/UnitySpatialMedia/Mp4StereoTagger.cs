using System;

namespace UnitySpatialMedia
{
    public static class Mp4StereoTagger
    {
        public enum StereoMode : byte
        {
            Mono = 0,
            TopBottom = 1,
            LeftRight = 2,
            StereoCustom = 3,
            RightLeft = 4
        }

        public static bool InjectSbsTag(
            string inputPath,
            string outputPath,
            StereoMode stereoMode = StereoMode.LeftRight,
            Action<string> console = null)
        {
            SpatialMediaMetadataInjector.StereoMode mapped;
            switch (stereoMode)
            {
                case StereoMode.Mono:
                    mapped = SpatialMediaMetadataInjector.StereoMode.Mono;
                    break;
                case StereoMode.TopBottom:
                    mapped = SpatialMediaMetadataInjector.StereoMode.TopBottom;
                    break;
                case StereoMode.LeftRight:
                    mapped = SpatialMediaMetadataInjector.StereoMode.LeftRight;
                    break;
                case StereoMode.RightLeft:
                    mapped = SpatialMediaMetadataInjector.StereoMode.RightLeft;
                    break;
                default:
                    mapped = SpatialMediaMetadataInjector.StereoMode.StereoCustom;
                    break;
            }

            return SpatialMediaMetadataInjector.InjectStereoOnly(inputPath, outputPath, mapped, console);
        }
    }
}
