using System;
using System.Collections.Generic;
using MonoDetour.HookGen;

[assembly: MonoDetourTargets(typeof(CaveDwellerAI), Members = ["Start"])]

namespace SCP682.Hooks;

static class CaveDwellerList
{
    public static List<CaveDwellerAI?> caveDwellerEnemies = [];

    internal static void Init()
    {
        Md.CaveDwellerAI.Start.Postfix(Postfix_CaveDwellerAI_Start);
    }

    private static void Postfix_CaveDwellerAI_Start(CaveDwellerAI self)
    {
        caveDwellerEnemies.Add(self);
    }

    public static IEnumerable<CaveDwellerAI> GetBabyCaveDwellers()
    {
        for (int i = 0; i < caveDwellerEnemies.Count; i++)
        {
            var caveDweller = caveDwellerEnemies[i];
            if (caveDweller == null || caveDweller.currentBehaviourStateIndex != 0) // 0 is baby state
            {
                caveDwellerEnemies.RemoveAt(i);
                i--;
                continue;
            }

            yield return caveDweller;
        }
    }
}
