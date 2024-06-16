namespace SCP682;

/// <summary>
/// The Plugin's logging class.
/// </summary>
static class P
{
    internal static void Log(object data) => Plugin.Logger.LogInfo(data);

    internal static void LogWarning(object data) => Plugin.Logger.LogWarning(data);

    internal static void LogError(object data) => Plugin.Logger.LogError(data);
}
