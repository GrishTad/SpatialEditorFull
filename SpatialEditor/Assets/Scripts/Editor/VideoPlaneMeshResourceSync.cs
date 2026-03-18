using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class VideoPlaneMeshResourceSync
{
    private const string SourceDir = "Assets/VideoPlaneMeshes";
    private const string ResourceDir = "Assets/Resources/VideoPlaneMeshes";

    static VideoPlaneMeshResourceSync()
    {
        SyncMeshesToResources();
    }

    [MenuItem("Tools/SpatialEditor/Sync VideoPlane Mesh Resources")]
    public static void SyncMeshesToResources()
    {
        if (!Directory.Exists(SourceDir))
        {
            return;
        }

        if (!Directory.Exists(ResourceDir))
        {
            Directory.CreateDirectory(ResourceDir);
        }

        bool changed = false;
        string[] sourceFiles = Directory.GetFiles(SourceDir, "*.fbx", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < sourceFiles.Length; i++)
        {
            string sourcePath = sourceFiles[i];
            string fileName = Path.GetFileName(sourcePath);
            string destinationPath = Path.Combine(ResourceDir, fileName);
            if (NeedsCopy(sourcePath, destinationPath))
            {
                File.Copy(sourcePath, destinationPath, true);
                changed = true;
            }
        }

        if (changed)
        {
            AssetDatabase.Refresh();
            Debug.Log("VideoPlaneMeshResourceSync: mirrored FBX meshes into Resources/VideoPlaneMeshes.");
        }
    }

    private static bool NeedsCopy(string sourcePath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            return true;
        }

        FileInfo sourceInfo = new FileInfo(sourcePath);
        FileInfo destinationInfo = new FileInfo(destinationPath);
        return sourceInfo.Length != destinationInfo.Length ||
            sourceInfo.LastWriteTimeUtc > destinationInfo.LastWriteTimeUtc;
    }
}

