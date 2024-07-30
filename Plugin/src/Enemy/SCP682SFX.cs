
using System.Collections.Generic;
using UnityEngine;

namespace SCP682.SCPEnemy;

public static class SFX
{
    internal static void InitializeSFX(AssetBundle assets)
    {
        // This whole thing might be stupid,
        // but I don't wanna depend on Unity just for serializing AudioClips.
        foreach (var clip in assets.LoadAllAssets<AudioClip>())
        {
            switch (clip.name.Split("SFX")[0])
            {
                case "bite": bite.Add(clip); break;
                case "hit": hit.Add(clip); break;
                case "run": run.Add(clip); break;
                case "roar": roar.Add(clip); break;
                case "walk": walk.Add(clip); break;
                case "defeated": defeated.Add(clip); break;
                case "jumpAttack": jumpAttack.Add(clip); break;
                case "swimDown": swimDown.Add(clip); break;
                case "wakeUp": wakeUp.Add(clip); break;
                default: Plugin.Logger.LogError($"AudioClip with name '{clip.name}' was not identified!"); break;
            }
        }
    }

    // Everything is a list so variations can be added for stuff that only have once variation.
    // Also I can get a random item from each list with the same code everywhere.
    public static List<AudioClip> bite = [];
    public static List<AudioClip> hit = [];
    public static List<AudioClip> run = [];
    public static List<AudioClip> roar = [];
    public static List<AudioClip> walk = [];
    public static List<AudioClip> defeated = [];
    public static List<AudioClip> jumpAttack = [];
    public static List<AudioClip> swimDown = [];
    public static List<AudioClip> wakeUp = [];
}