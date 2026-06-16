using Microsoft.Build.Locator;

namespace Codex.Roslyn.Workspaces;

public static class MSBuildRegistration
{
    private static readonly object Sync = new();
    private static bool registered;

    public static void RegisterDefaults()
    {
        if (registered || MSBuildLocator.IsRegistered)
        {
            registered = true;
            return;
        }

        lock (Sync)
        {
            if (registered || MSBuildLocator.IsRegistered)
            {
                registered = true;
                return;
            }

            MSBuildLocator.RegisterDefaults();
            registered = true;
        }
    }
}
