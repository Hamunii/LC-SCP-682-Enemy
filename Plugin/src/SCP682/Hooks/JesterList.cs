using System;
using System.Collections.Generic;
using MonoDetour.HookGen;

[assembly: MonoDetourTargets(typeof(JesterAI), Members = ["Start"])]

namespace SCP682.Hooks;

static class JesterListHook
{
    public static List<JesterAI?> jesterEnemies = [];

    internal static void Init()
    {
        Md.JesterAI.Start.Postfix(Postfix_JesterAI_Start);
    }

    private static void Postfix_JesterAI_Start(JesterAI self)
    {
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
