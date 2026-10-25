using UnityEditor;

namespace EjoyFramework.Tests
{
    public static class LocalPlayModeConfiguration
    {
        public static void DisableDomainReload()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }
    }
}
