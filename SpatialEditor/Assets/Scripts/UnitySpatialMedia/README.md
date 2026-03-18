# Unity Spatial Media (C# Port)

This folder contains a Unity C# port of the Python `spatialmedia` MP4 injector logic:

- MP4 box parsing/writing with `stco` and `co64` offset updates.
- V1 spherical XML injection (`uuid` box).
- V2 video metadata injection (`st3d`, `sv3d/proj/prhd/equi`).
- Spatial audio `SA3D` injection checks (matching the Python workflow).
- Metadata parsing helpers for existing files.

## Files

- `SpatialMediaMetadataInjector.cs`: main API.
- `Mp4StereoTagger.cs`: compatibility API with your original script style.
- `Internal/Mp4Model.cs`: MP4 model/parser/writer internals.
- `Examples/SpatialMediaUsageExample.cs`: ready-to-run Unity example script.

## Quick Usage

```csharp
// 1) Stereo-only tag (st3d), similar to your original script:
bool ok1 = Mp4StereoTagger.InjectSbsTag(
    inputPath,
    outputPath,
    Mp4StereoTagger.StereoMode.LeftRight,
    Debug.Log);

// 2) V2 injection (st3d + sv3d/proj/equi):
bool ok2 = SpatialMediaMetadataInjector.InjectV2(
    inputPath,
    outputPath,
    stereoMode: SpatialMediaMetadataInjector.StereoLayout.LeftRight,
    projection: SpatialMediaMetadataInjector.ProjectionMode.Equirectangular,
    bounds: "0:0:0:0",
    console: Debug.Log);

// 3) Full metadata object (V1 XML + V2 + optional audio):
var metadata = new SpatialMediaMetadataInjector.Metadata(
    SpatialMediaMetadataInjector.ProjectionMode.Equirectangular,
    SpatialMediaMetadataInjector.StereoLayout.LeftRight,
    "0:0:0:0");
metadata.VideoXml = SpatialMediaMetadataInjector.GenerateSphericalXml(
    SpatialMediaMetadataInjector.ProjectionMode.Equirectangular,
    SpatialMediaMetadataInjector.StereoLayout.LeftRight);
bool ok3 = SpatialMediaMetadataInjector.InjectMetadata(inputPath, outputPath, metadata, Debug.Log);

// 4) Parse metadata:
var parsed = SpatialMediaMetadataInjector.ParseMetadata(inputPath, Debug.Log);
```

## Preset Examples (`vr360`, `vr180`, `flat3d`)

```csharp
// VR360 stereo (left-right)
bool vr360 = SpatialMediaMetadataInjector.InjectPreset(
    inputPath,
    outputPathVr360,
    SpatialMediaMetadataInjector.VideoPreset.Vr360LeftRight,
    includeV1Xml: true,
    console: Debug.Log);

// VR180 stereo (left-right), with default bounds:
// 0:0:0x40000000:0x40000000
bool vr180 = SpatialMediaMetadataInjector.InjectPreset(
    inputPath,
    outputPathVr180,
    SpatialMediaMetadataInjector.VideoPreset.Vr180LeftRight,
    includeV1Xml: true,
    vr180Bounds: SpatialMediaMetadataInjector.DefaultVr180Bounds,
    console: Debug.Log);

// Flat 3D (st3d only, no equirectangular projection metadata)
bool flat3d = SpatialMediaMetadataInjector.InjectPreset(
    inputPath,
    outputPathFlat3d,
    SpatialMediaMetadataInjector.VideoPreset.Flat3dLeftRight,
    includeV1Xml: false,
    console: Debug.Log);
```

Available presets:
- `Vr360Mono`
- `Vr360LeftRight`
- `Vr360TopBottom`
- `Vr180LeftRight`
- `Vr180RightLeft`
- `Flat3dLeftRight`
- `Flat3dTopBottom`

## Notes

- Input and output must be different files.
- Supported extensions are `.mp4` and `.mov`.
- String-based APIs are still supported, but enum overloads are available for Inspector-friendly selection.
- The parser/writer follows the same structural strategy as the Python project:
  parse boxes -> mutate tree -> resize containers -> write out with offset delta.
