using UnityEngine;

public static class AndroidJobPolling
{
    public static bool TryParseJobState(string stateJson, out AndroidJobStateResponse response)
    {
        response = null;
        if (string.IsNullOrEmpty(stateJson))
        {
            return false;
        }

        try
        {
            response = JsonUtility.FromJson<AndroidJobStateResponse>(stateJson);
            return response != null && response.ok && response.state != null;
        }
        catch
        {
            response = null;
            return false;
        }
    }

    public static bool IsTerminalStatus(string status)
    {
        string normalized = status == null ? string.Empty : status.ToLowerInvariant();
        return normalized == "succeeded" || normalized == "failed" || normalized == "canceled";
    }
}

