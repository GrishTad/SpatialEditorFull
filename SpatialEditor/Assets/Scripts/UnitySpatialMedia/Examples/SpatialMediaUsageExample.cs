using System.IO;
using UnityEngine;

namespace UnitySpatialMedia.Examples
{
    public class SpatialMediaUsageExample : MonoBehaviour
    {
        [Header("Input / Output")]
        public string InputFileName = "input.mp4";
        public string OutputFileName = "output_tagged.mp4";

        [Header("Enum Selectors (Inspector)")]
        public SpatialMediaMetadataInjector.StereoLayout StereoLayout =
            SpatialMediaMetadataInjector.StereoLayout.LeftRight;
        public SpatialMediaMetadataInjector.ProjectionMode ProjectionMode =
            SpatialMediaMetadataInjector.ProjectionMode.Equirectangular;
        public string Bounds = "0:0:0:0";

        [Header("Preset Selectors")]
        public SpatialMediaMetadataInjector.VideoPreset Preset =
            SpatialMediaMetadataInjector.VideoPreset.Vr360LeftRight;
        public bool IncludeV1Xml;
        public string Vr180BoundsOverride = SpatialMediaMetadataInjector.DefaultVr180Bounds;

        [ContextMenu("Inject Stereo (st3d only)")]
        public void InjectStereoOnly()
        {
            string input = Path.Combine(Application.persistentDataPath, InputFileName);
            string output = Path.Combine(Application.persistentDataPath, OutputFileName);

            bool ok = Mp4StereoTagger.InjectSbsTag(
                input,
                output,
                Mp4StereoTagger.StereoMode.LeftRight,
                msg => Debug.Log(msg));

            Debug.Log("InjectStereoOnly result: " + ok);
        }

        [ContextMenu("Inject V2 (Enum Selectors)")]
        public void InjectV2WithEnums()
        {
            string input = Path.Combine(Application.persistentDataPath, InputFileName);
            string output = Path.Combine(Application.persistentDataPath, OutputFileName);

            bool ok = SpatialMediaMetadataInjector.InjectV2(
                input,
                output,
                stereoMode: StereoLayout,
                projection: ProjectionMode,
                bounds: Bounds,
                console: msg => Debug.Log(msg));

            Debug.Log("InjectV2WithEnums result: " + ok);
        }

        [ContextMenu("Inject Selected Preset")]
        public void InjectSelectedPreset()
        {
            string input = Path.Combine(Application.persistentDataPath, InputFileName);
            string output = Path.Combine(Application.persistentDataPath, OutputFileName);

            bool ok = SpatialMediaMetadataInjector.InjectPreset(
                input,
                output,
                preset: Preset,
                includeV1Xml: IncludeV1Xml,
                vr180Bounds: Vr180BoundsOverride,
                crop: null,
                console: msg => Debug.Log(msg));

            Debug.Log("InjectSelectedPreset result: " + ok);
        }

        [ContextMenu("Preset Example: VR360 Left-Right")]
        public void ExampleVr360()
        {
            RunPreset(SpatialMediaMetadataInjector.VideoPreset.Vr360LeftRight);
        }

        [ContextMenu("Preset Example: VR180 Left-Right")]
        public void ExampleVr180()
        {
            RunPreset(SpatialMediaMetadataInjector.VideoPreset.Vr180LeftRight);
        }

        [ContextMenu("Preset Example: Flat3D Left-Right")]
        public void ExampleFlat3d()
        {
            RunPreset(SpatialMediaMetadataInjector.VideoPreset.Flat3dLeftRight);
        }

        [ContextMenu("Read Metadata")]
        public void ReadMetadata()
        {
            string input = Path.Combine(Application.persistentDataPath, InputFileName);
            SpatialMediaMetadataInjector.ParsedMetadata parsed =
                SpatialMediaMetadataInjector.ParseMetadata(input, msg => Debug.Log(msg));

            if (parsed == null)
            {
                Debug.LogError("Parse failed.");
                return;
            }

            Debug.Log("V1 video tracks: " + parsed.VideoV1.Count);
            Debug.Log("V2 video tracks: " + parsed.VideoV2.Count);
            Debug.Log("Audio channels: " + parsed.NumAudioChannels);
        }

        private void RunPreset(SpatialMediaMetadataInjector.VideoPreset preset)
        {
            string input = Path.Combine(Application.persistentDataPath, InputFileName);
            string output = Path.Combine(Application.persistentDataPath, OutputFileName);

            bool ok = SpatialMediaMetadataInjector.InjectPreset(
                input,
                output,
                preset: preset,
                includeV1Xml: IncludeV1Xml,
                vr180Bounds: Vr180BoundsOverride,
                crop: null,
                console: msg => Debug.Log(msg));

            Debug.Log($"RunPreset ({preset}) result: {ok}");
        }
    }
}
