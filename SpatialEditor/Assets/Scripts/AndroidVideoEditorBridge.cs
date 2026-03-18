using System;
using System.Threading;
using UnityEngine;

[Serializable]
public class AndroidApiError
{
    public string code;
    public string message;
    public string causeClass;
}

[Serializable]
public class AndroidValidationIssue
{
    public string code;
    public string message;
    public string field;
}

[Serializable]
public class AndroidValidationReport
{
    public AndroidValidationIssue[] errors;
    public AndroidValidationIssue[] warnings;
    public string[] downgradedFeatures;
    public long normalizedDurationMs;
}

[Serializable]
public class AndroidInitializeResponse
{
    public bool ok;
    public string libraryVersion;
    public int schemaVersion;
    public string[] supportedFeatures;
    public string configStatus;
    public AndroidApiError error;
}

[Serializable]
public class AndroidValidateResponse
{
    public bool ok;
    public string libraryVersion;
    public int schemaVersion;
    public AndroidValidationReport report;
    public AndroidApiError error;
}

[Serializable]
public class AndroidStartExportResponse
{
    public bool ok;
    public string libraryVersion;
    public int schemaVersion;
    public string jobId;
    public bool accepted;
    public string initialState;
    public AndroidApiError error;
}

[Serializable]
public class AndroidJobStatePayload
{
    public string jobId;
    public string status;
    public int progressPercent;
    public string outputUri;
    public AndroidApiError error;
    public AndroidValidationIssue[] warnings;
}

[Serializable]
public class AndroidJobStateResponse
{
    public bool ok;
    public string libraryVersion;
    public int schemaVersion;
    public AndroidJobStatePayload state;
    public AndroidApiError error;
}

[Serializable]
public class AndroidAckResponse
{
    public bool ok;
    public string libraryVersion;
    public int schemaVersion;
    public bool acknowledged;
    public string message;
    public AndroidApiError error;
}

[Serializable]
public class AndroidGalleryQueryConfig
{
    public int limit = 200;
    public string bucketId;
    public bool includePending;
}

[Serializable]
public class AndroidGalleryVideoEntry
{
    public string uri;
    public string displayName;
    public long durationMs;
    public long sizeBytes;
    public string mimeType;
    public long dateAddedSec;
}

[Serializable]
public class AndroidGalleryQueryResponse
{
    public bool ok;
    public AndroidGalleryVideoEntry[] videos;
    public AndroidApiError error;
}

[Serializable]
public class AndroidCreateOutputUriConfig
{
    public string displayName;
    public string mimeType = "video/mp4";
    public string relativePath = "Movies/SpatialEditor";
}

[Serializable]
public class AndroidCreateOutputUriResponse
{
    public bool ok;
    public string outputUri;
    public AndroidApiError error;
}

public static class AndroidVideoEditorBridge
{
    private const string EditorApiClassName = "com.ocutech.editor.api.UnityVideoEditor";
    private const string MediaStoreApiClassName = "com.ocutech.editor.api.UnityMediaStoreBridge";
    private const string RuntimeClassName = "com.ocutech.editor.api.UnityVideoEditor$Runtime";
    private const string ExportRuntimeConfigClassName = "com.ocutech.editor.export.ExportRuntimeConfig";
    private const string ExportJobManagerClassName = "com.ocutech.editor.export.ExportJobManager";
    private const string Media3ExportExecutorClassName = "com.ocutech.editor.export.Media3ExportExecutor";
    private const string ThreadPoolDispatcherKtClassName = "kotlinx.coroutines.ThreadPoolDispatcherKt";

    private static bool runtimePatchedForSingleThreadExport;

    public static bool IsAndroidRuntime
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    public static string Initialize(string configJson)
    {
        return CallStatic(EditorApiClassName, "initialize", configJson);
    }

    public static string ValidateProject(string projectJson)
    {
        return CallStatic(EditorApiClassName, "validateProject", projectJson);
    }

    public static string StartExport(string projectJson)
    {
        return CallStatic(EditorApiClassName, "startExport", projectJson);
    }

    public static string GetJobState(string jobId)
    {
        return CallStatic(EditorApiClassName, "getJobState", jobId);
    }

    public static string CancelJob(string jobId)
    {
        return CallStatic(EditorApiClassName, "cancelJob", jobId);
    }

    public static string ReleaseJob(string jobId)
    {
        return CallStatic(EditorApiClassName, "releaseJob", jobId);
    }

    public static string ReleaseAll()
    {
        return CallStatic(EditorApiClassName, "releaseAll", "");
    }

    public static string QueryGalleryVideos(AndroidGalleryQueryConfig config)
    {
        string configJson = JsonUtility.ToJson(config ?? new AndroidGalleryQueryConfig());
        return CallStatic(MediaStoreApiClassName, "queryGalleryVideos", configJson);
    }

    public static string CreateOutputVideoUri(AndroidCreateOutputUriConfig config)
    {
        string configJson = JsonUtility.ToJson(config ?? new AndroidCreateOutputUriConfig());
        return CallStatic(MediaStoreApiClassName, "createOutputVideoUri", configJson);
    }

    private static string CallStatic(string className, string methodName, string payload)
    {
        if (!IsAndroidRuntime)
        {
            return "{\"ok\":false,\"error\":{\"code\":\"UNSUPPORTED_PLATFORM\",\"message\":\"Android runtime only.\"}}";
        }

        try
        {
            return CallStaticOnAndroidUiThread(className, methodName, payload);
        }
        catch (Exception ex)
        {
            Debug.LogError($"AndroidVideoEditorBridge::{methodName} failed: {ex}");
            string safe = ex.ToString().Replace('"', '\'');
            return "{\"ok\":false,\"error\":{\"code\":\"BRIDGE_CALL_FAILED\",\"message\":\"" + safe + "\"}}";
        }
    }

    private static string CallStaticOnAndroidUiThread(string className, string methodName, string payload)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string result = null;
        Exception callException = null;
        using var doneEvent = new ManualResetEventSlim(false);

        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        {
            if (activity == null)
            {
                throw new InvalidOperationException("UnityPlayer.currentActivity is null.");
            }

            AndroidJavaRunnable invoke = new AndroidJavaRunnable(() =>
            {
                try
                {
                    PrepareJavaThreadClassLoader();
                    TryWarmupClassForLoader(className);

                    if (string.Equals(className, EditorApiClassName, StringComparison.Ordinal) &&
                        string.Equals(methodName, "initialize", StringComparison.Ordinal))
                    {
                        runtimePatchedForSingleThreadExport = false;
                    }

                    if (string.Equals(className, EditorApiClassName, StringComparison.Ordinal) &&
                        !runtimePatchedForSingleThreadExport &&
                        !string.Equals(methodName, "initialize", StringComparison.Ordinal))
                    {
                        TryPatchRuntimeForSingleThreadExportManager();
                    }

                    using (var clazz = new AndroidJavaClass(className))
                    {
                        if (string.IsNullOrEmpty(payload) && methodName == "releaseAll")
                        {
                            result = clazz.CallStatic<string>(methodName);
                        }
                        else
                        {
                            result = clazz.CallStatic<string>(methodName, payload ?? string.Empty);
                        }
                    }

                    if (string.Equals(className, EditorApiClassName, StringComparison.Ordinal) &&
                        string.Equals(methodName, "initialize", StringComparison.Ordinal))
                    {
                        TryPatchRuntimeForSingleThreadExportManager();
                    }
                }
                catch (Exception ex)
                {
                    callException = ex;
                }
                finally
                {
                    doneEvent.Set();
                }
            });

            if (IsOnAndroidMainThread())
            {
                invoke();
            }
            else
            {
                activity.Call("runOnUiThread", invoke);
            }

            const int timeoutMs = 30000;
            if (!doneEvent.Wait(timeoutMs))
            {
                throw new TimeoutException("Timed out waiting for Android UI thread bridge call.");
            }
        }

        if (callException != null)
        {
            throw callException;
        }

        return result;
#else
        return "{\"ok\":false,\"error\":{\"code\":\"UNSUPPORTED_PLATFORM\",\"message\":\"Android runtime only.\"}}";
#endif
    }

    private static void TryPatchRuntimeForSingleThreadExportManager()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (runtimePatchedForSingleThreadExport)
        {
            return;
        }

        try
        {
            using (var classClass = new AndroidJavaClass("java.lang.Class"))
            using (var editorClass = classClass.CallStatic<AndroidJavaObject>("forName", EditorApiClassName))
            using (var runtimeField = editorClass.Call<AndroidJavaObject>("getDeclaredField", "runtime"))
            {
                runtimeField.Call("setAccessible", true);

                using (var runtime = runtimeField.Call<AndroidJavaObject>("get", new object[] { null }))
                {
                    if (runtime == null)
                    {
                        return;
                    }

                    using (var config = runtime.Call<AndroidJavaObject>("getConfig"))
                    using (var planner = runtime.Call<AndroidJavaObject>("getPlanner"))
                    using (var context = runtime.Call<AndroidJavaObject>("getContext"))
                    {
                        if (config == null || planner == null || context == null)
                        {
                            return;
                        }

                        bool keepLastRequest = config.Call<bool>("getKeepLastRequest");
                        bool debugLogging = config.Call<bool>("getDebugLogging");

                        using (var runtimeConfig = new AndroidJavaObject(
                                   ExportRuntimeConfigClassName,
                                   "0.1.0",
                                   1,
                                   keepLastRequest,
                                   debugLogging))
                        using (var exportExecutor = new AndroidJavaObject(Media3ExportExecutorClassName, context, runtimeConfig))
                        using (var threadPoolDispatcher = new AndroidJavaClass(ThreadPoolDispatcherKtClassName))
                        using (var singleThreadDispatcher = threadPoolDispatcher.CallStatic<AndroidJavaObject>("newSingleThreadContext", "ocutech-export-single"))
                        using (var manager = new AndroidJavaObject(
                                   ExportJobManagerClassName,
                                   context,
                                   runtimeConfig,
                                   planner,
                                   exportExecutor,
                                   singleThreadDispatcher))
                        using (var patchedRuntime = new AndroidJavaObject(RuntimeClassName, config, manager, planner, context))
                        {
                            try
                            {
                                using (var oldManager = runtime.Call<AndroidJavaObject>("getManager"))
                                {
                                    if (oldManager != null)
                                    {
                                        oldManager.Call<int>("releaseAll");
                                    }
                                }
                            }
                            catch (Exception releaseEx)
                            {
                                Debug.LogWarning("AndroidVideoEditorBridge::TryPatchRuntime release warning: " + releaseEx.Message);
                            }

                            runtimeField.Call("set", new object[] { null, patchedRuntime });
                            runtimePatchedForSingleThreadExport = true;
                            Debug.Log("AndroidVideoEditorBridge: Applied single-thread export runtime patch.");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("AndroidVideoEditorBridge::TryPatchRuntimeForSingleThreadExportManager warning: " + ex.Message);
        }
#endif
    }

    private static bool IsOnAndroidMainThread()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var looperClass = new AndroidJavaClass("android.os.Looper"))
            using (var myLooper = looperClass.CallStatic<AndroidJavaObject>("myLooper"))
            using (var mainLooper = looperClass.CallStatic<AndroidJavaObject>("getMainLooper"))
            {
                if (myLooper == null || mainLooper == null)
                {
                    return false;
                }

                return myLooper.Call<bool>("equals", mainLooper);
            }
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private static void PrepareJavaThreadClassLoader()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null)
                {
                    return;
                }

                using (var classLoader = activity.Call<AndroidJavaObject>("getClassLoader"))
                using (var threadClass = new AndroidJavaClass("java.lang.Thread"))
                using (var currentThread = threadClass.CallStatic<AndroidJavaObject>("currentThread"))
                {
                    currentThread.Call("setContextClassLoader", classLoader);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("AndroidVideoEditorBridge::PrepareJavaThreadClassLoader warning: " + ex.Message);
        }
#endif
    }

    private static void TryWarmupClassForLoader(string className)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null)
                {
                    return;
                }

                using (var classLoader = activity.Call<AndroidJavaObject>("getClassLoader"))
                using (var classClass = new AndroidJavaClass("java.lang.Class"))
                {
                    classClass.CallStatic<AndroidJavaObject>("forName", className, true, classLoader);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("AndroidVideoEditorBridge::TryWarmupClassForLoader warning: " + ex.Message);
        }
#endif
    }
}
