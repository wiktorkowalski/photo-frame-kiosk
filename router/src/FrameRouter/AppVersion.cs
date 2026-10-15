using System.Reflection;

namespace FrameRouter;

internal static class AppVersion
{
    // The frame page reloads itself when this changes, so a deploy reaches the frames without ADB.
    public static readonly string Value =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
}
