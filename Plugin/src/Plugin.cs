using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using LethalLib.Modules;
using MonoMod.RuntimeDetour.HookGen;
using SCP682.Configuration;
using SCP682.Hooks;
using SCP682.SCPEnemy;
using UnityEngine;

namespace SCP682;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInDependency(LethalLib.Plugin.ModGUID)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger = null!;
    internal static PluginConfig BoundConfig { get; private set; } = null!;
    public static AssetBundle? modAssets;
    internal static EnemyType SCP682ET = null!;

    private void Awake()
    {
        Logger = base.Logger;
        var watch = Stopwatch.StartNew();
        Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} {PluginInfo.PLUGIN_VERSION} started loading...");

        // If you don't want your mod to use a configuration file, you can remove this line, Configuration.cs, and other references.
        BoundConfig = new PluginConfig(base.Config);

        // This should be ran before Network Prefabs are registered.
        InitializeNetworkBehaviours();

        // We load the asset bundle that should be next to our DLL file, with the specified name.
        // You may want to rename your asset bundle from the AssetBundle Browser in order to avoid an issue with
        // asset bundle identifiers being the same between multiple bundles, allowing the loading of only one bundle from one mod.
        // In that case also remember to change the asset bundle copying code in the csproj.user file.
        const string mainBundleName = "scp682assets";
#pragma warning disable CS0219 // Variable is assigned but its value is never used
        const string videoBundleName = "scp682videobundle";
#pragma warning restore CS0219 // Variable is assigned but its value is never used
        const string assetsDirName = "SCP682Assets";
#if DEBUG
        var assetsDir = Path.Combine(Paths.BepInExRootPath, "scripts", assetsDirName);
#else
        var assetsDir = Path.Combine(Path.GetDirectoryName(Info.Location), bundleDirName);
#endif
        modAssets = AssetBundle.LoadFromFile(Path.Combine(assetsDir, mainBundleName));
        if (modAssets is null)
        {
            Logger.LogError($"Failed to load custom assets.");
            return;
        }

        SFX.InitializeSFX(modAssets);
        SCP682ET = modAssets.LoadAsset<EnemyType>("SCP682ET");
        var SCP682TN = modAssets.LoadAsset<TerminalNode>("SCP682TN");
#if !DEBUG // Save reload time by not loading the video
        var videoAssets = AssetBundle.LoadFromFile(Path.Combine(assetsDir, videoBundleName));
        var terminalSpinVid = videoAssets.LoadAsset<VideoClip>("SCP682Spin");
        SCP682TN.displayVideo = terminalSpinVid;
#endif

        AddEnemyScript.SCP682AI(SCP682ET, modAssets);

        // Network Prefabs need to be registered. See https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/
        // LethalLib registers prefabs on GameNetworkManager.Start.
#if !DEBUG
        NetworkPrefabs.RegisterNetworkPrefab(SCP682ET.enemyPrefab);
#else
        if (!Enemies.spawnableEnemies.Any(enemy => enemy.enemy.enemyName.Equals("SCP682")))
#endif
            Enemies.RegisterEnemy(
                SCP682ET,
                BoundConfig.SpawnWeight.Value,
                Levels.LevelTypes.All,
                SCP682TN
            );
#if DEBUG
        // We probably want the enemy to instantly spawn in front of us if possible
        if (StartOfRound.Instance is not null)
        {
            Vector3 spawnPosition =
                GameNetworkManager.Instance.localPlayerController.transform.position
                - Vector3.Scale(
                    new Vector3(-5, 0, -5),
                    GameNetworkManager.Instance.localPlayerController.transform.forward
                );
            RoundManager.Instance.SpawnEnemyGameObject(spawnPosition, 0f, -1, SCP682ET);
        }
#endif
        JesterListHook.Init();

        watch.Stop();
        Logger.LogInfo($"{PluginInfo.PLUGIN_GUID} loaded in {watch.ElapsedMilliseconds}ms!");
    }

    // We should clean up our resources when reloading the plugin.
    private void OnDestroy()
    {
        SCP682ET.enemyPrefab.RemoveComponent<SCP682AI>();
        modAssets?.Unload(true);

        SCP682AI.SCP682Objects.ForEach(Destroy);
        SCP682AI.SCP682Objects.Clear();

        HookEndpointManager.RemoveAllOwnedBy(Assembly.GetExecutingAssembly());
#if DEBUG
        if (ModMenuAPICompatibility.Enabled)
            ModMenuAPICompatibility.ClearMenus();
#endif
        Logger.LogInfo("Cleaned all resources!");
    }

    private static void InitializeNetworkBehaviours()
    {
        // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching

        Type[] types;
        try
        {
			types = Assembly.GetExecutingAssembly().GetTypes();
		}
        catch(ReflectionTypeLoadException ex)
        {
			types = ex.Types.Where(t => t != null).ToArray();
		}
        foreach (var type in types)
        {
            var methods = type.GetMethods(
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            );
            foreach (var method in methods)
            {
                var attributes = method.GetCustomAttributes(
                    typeof(RuntimeInitializeOnLoadMethodAttribute),
                    false
                );
                if (attributes.Length > 0)
                {
                    method.Invoke(null, null);
                }
            }
        }
    }
}
