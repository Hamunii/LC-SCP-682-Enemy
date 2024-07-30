using System;
using System.Runtime.CompilerServices;
using ModMenuAPI.ModMenuItems;
using SCP682;
using SCP682.SCPEnemy;

static class ModMenuAPICompatibility
{
    internal static bool Enabled => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("Hamunii.ModMenuAPI");

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void InitDebug(SCP682AI instance)
    {
        MMButtonMenuInstantiable mmMenu = new("Override State >");

        new ModMenu("SCP-682 Debug")
            .RegisterItem(new DebugNewSearchRoutineAction(instance))
            .RegisterItem(mmMenu)
            .RegisterItem(new DebugPrintInfoAction(instance));

        mmMenu.MenuItems.Add(new DebugOverrideState(instance, "WanderToShipState"));
        mmMenu.MenuItems.Add(new DebugOverrideState(instance, "OnShipAmbushState"));
        mmMenu.MenuItems.Add(new DebugOverrideState(instance, "WanderThroughEntranceState"));
        mmMenu.MenuItems.Add(new DebugOverrideState(instance, "AtFacilityWanderingState"));
    }

    class DebugPrintInfoAction(SCP682AI self) : MMButtonAction("Print Info")
    {
        protected override void OnClick()
        {
            PLog.Log($"Current State: {self.ActiveState}");
        }
    }

    class DebugNewSearchRoutineAction(SCP682AI self) : MMButtonAction("New Search Routine")
    {
        protected override void OnClick() => self.StartSearch(self.transform.position);
    }

    class DebugOverrideState(SCP682AI self, string state) : MMButtonAction($"{state}")
    {
        protected override void OnClick()
        {
            if (self.isEnemyDead) return;
            self.TransitionStateServerRpc(state);
        }
    }
}