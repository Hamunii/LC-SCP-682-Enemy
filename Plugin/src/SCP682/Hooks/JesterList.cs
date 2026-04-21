using System.Collections.Generic;

namespace SCP682.Hooks;

static class JesterListHook
{
    public static List<JesterAI?> jesterEnemies = [];

    internal static void Init()
    {
        On.JesterAI.Start += JesterAI_Start;
    }

    private static void JesterAI_Start(On.JesterAI.orig_Start orig, JesterAI self)
    {
        orig(self);
        jesterEnemies.Add(self);
    }

    public static IEnumerable<JesterAI> GetJesters()
    {
        for (int i = 0; i < JesterListHook.jesterEnemies.Count; i++)
        {
            var jester = JesterListHook.jesterEnemies[i];
            if (jester == null)
            {
                JesterListHook.jesterEnemies.RemoveAt(i);
                i--;
                continue;
            }
            yield return jester;
        }
    }
}
