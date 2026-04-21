using System;
using System.Collections.Generic;

namespace SCP682.Hooks;

static class CaveDwellerList
{
    public static List<CaveDwellerAI?> caveDwellerEnemies = [];

    internal static void Init()
    {
        On.CaveDwellerAI.Start += CaveDwellerAI_Start;
    }

    private static void CaveDwellerAI_Start(On.CaveDwellerAI.orig_Start orig, CaveDwellerAI self)
    {
        orig(self);
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
