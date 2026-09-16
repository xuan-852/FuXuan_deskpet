using System.IO;

// Product decision A (2026-09-16): legacy parameter-generated motions are
// offline-only until an equivalent semantic skill has completed certification.
public static class LegacyGeneratedMotionPolicy
{
    public static bool TryAllowOfflineTest(out string reason)
    {
        if (File.Exists(DataPathConfig.TestModeFile))
        {
            reason = "isolated-test-mode";
            return true;
        }

        reason = "legacy-generated-motion-not-certified";
        return false;
    }
}
