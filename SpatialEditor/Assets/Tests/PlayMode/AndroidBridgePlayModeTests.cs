using System;
using NUnit.Framework;

public class AndroidBridgePlayModeTests
{
    [Test]
    public void BridgeReturnsStructuredErrorOnNonAndroidRuntime()
    {
        Type bridgeType = ResolveType("AndroidVideoEditorBridge");
        Assert.NotNull(bridgeType, "AndroidVideoEditorBridge type not found.");

        var initialize = bridgeType.GetMethod("Initialize");
        Assert.NotNull(initialize);

        string response = initialize.Invoke(null, new object[] { "{}" }) as string;
        Assert.NotNull(response);
        Assert.IsTrue(response.Contains("\"ok\":false"));
        Assert.IsTrue(response.Contains("UNSUPPORTED_PLATFORM") || response.Contains("BRIDGE_CALL_FAILED"));
    }

    [Test]
    public void PollingUtilityIdentifiesTerminalStatuses()
    {
        Type pollingType = ResolveType("AndroidJobPolling");
        Assert.NotNull(pollingType, "AndroidJobPolling type not found.");

        var isTerminal = pollingType.GetMethod("IsTerminalStatus");
        Assert.NotNull(isTerminal);

        Assert.IsTrue((bool)isTerminal.Invoke(null, new object[] { "succeeded" }));
        Assert.IsTrue((bool)isTerminal.Invoke(null, new object[] { "failed" }));
        Assert.IsTrue((bool)isTerminal.Invoke(null, new object[] { "canceled" }));
        Assert.IsFalse((bool)isTerminal.Invoke(null, new object[] { "running" }));
    }

    [Test]
    public void PollingUtilityParsesValidStateJson()
    {
        Type pollingType = ResolveType("AndroidJobPolling");
        Assert.NotNull(pollingType, "AndroidJobPolling type not found.");

        const string stateJson = "{\"ok\":true,\"state\":{\"jobId\":\"j1\",\"status\":\"running\",\"progressPercent\":35}}";
        var tryParse = pollingType.GetMethod("TryParseJobState");
        Assert.NotNull(tryParse);

        object[] args = { stateJson, null };
        bool parsed = (bool)tryParse.Invoke(null, args);
        Assert.IsTrue(parsed);

        object response = args[1];
        Assert.NotNull(response);
        var stateField = response.GetType().GetField("state");
        Assert.NotNull(stateField);

        object state = stateField.GetValue(response);
        Assert.NotNull(state);
        string jobId = state.GetType().GetField("jobId")?.GetValue(state) as string;
        string status = state.GetType().GetField("status")?.GetValue(state) as string;
        Assert.AreEqual("j1", jobId);
        Assert.AreEqual("running", status);
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

