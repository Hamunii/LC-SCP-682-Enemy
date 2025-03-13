#if DEBUG || true
using System.Reflection;
using System.Runtime.CompilerServices;
using ModMenuAPI.ModMenuItems;
using SCP682;
using SCP682.Hooks;
using SCP682.SCPEnemy;
using UnityEngine;

static class ModMenuAPICompatibility
{
    internal static bool Enabled => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("Hamunii.ModMenuAPI");

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void InitDebug(SCP682AI instance)
    {
        MMButtonMenuInstantiable mmMenu = new("Override State >");
        MMButtonMenuInstantiable mmMenu2 = new("Spawn 682 >");

        new ModMenu("SCP-682 Debug")
            .RegisterItem(new DebugNewSearchRoutineAction(instance))
            .RegisterItem(mmMenu)
            .RegisterItem(new DebugPrintInfoAction(instance))
            .RegisterItem(mmMenu2)
            .RegisterItem(new Kill682Action());

        mmMenu.MenuItems.Add(new DebugOverrideState(instance, "SCP682.SCPEnemy.SCP682AI+WanderToShipState"));
        mmMenu.MenuItems.Add(new DebugOverrideState(instance, "SCP682.SCPEnemy.SCP682AI+OnShipAmbushState"));
        mmMenu.MenuItems.Add(new DebugOverrideState(instance, "SCP682.SCPEnemy.SCP682AI+WanderThroughEntranceState"));
        mmMenu.MenuItems.Add(new DebugOverrideState(instance, "SCP682.SCPEnemy.SCP682AI+AtFacilityWanderingState"));

        mmMenu2.MenuItems.Add(new SpawnSCP682Action(true));
        mmMenu2.MenuItems.Add(new SpawnSCP682Action(false));
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void ClearMenus() =>
        ModMenu.RemoveAllOwnedBy(Assembly.GetExecutingAssembly());

    class DebugPrintInfoAction(SCP682AI self) : MMButtonAction("Print Info")
    {
        protected override void OnClick()
        {
            PLog.Log($"Current State: {self.activeState}");
        }
    }

    class DebugNewSearchRoutineAction(SCP682AI self) : MMButtonAction("New Search Routine")
    {
        protected override void OnClick() => self.StartSearch(self.transform.position);
    }

    class DebugOverrideState(SCP682AI self, string state) : MMButtonAction($"{state.Split('+')[1]}")
    {
        protected override void OnClick()
        {
            if (self.isEnemyDead) return;
            self.TransitionStateServerRpc(state, new System.Random().Next());
        }
    }

    class SpawnSCP682Action(bool outside) : MMButtonAction($"Spawn " + (outside ? "outside" : "inside"))
    {
        protected override void OnClick()
        {
            new Kill682Action().CommonInvoke();
            ApparatusTakenHook.SpawnSCP682(outside);
        }
    }

    class Kill682Action() : MMButtonAction("Kill 682 instances")
    {
        protected override void OnClick()
        {
            SCP682AI.SCP682Objects.ForEach(Object.Destroy);
            SCP682AI.SCP682Objects.Clear();
            ClearMenus();
        }
    }
}
#endif