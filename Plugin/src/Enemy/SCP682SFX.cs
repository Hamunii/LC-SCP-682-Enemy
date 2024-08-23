
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
                case "spawn": spawn.Add(clip); break;
                default: InitializeVoiceLine(clip); break;
                // Plugin.Logger.LogError($"AudioClip with name '{clip.name}' was not identified!"); break;
            }
        }
    }

    private static void InitializeVoiceLine(AudioClip clip)
    {
        switch (clip.name)
        {
            case "Worms_SCP682": Voice.Worms_EngageIndoorEnemies = clip; break;
            // case "Worms_SCP682": Voice.Bothersome_EngageBaboonHawk = clip; break; // Didn't find clip
            case "Silence_SCP682": Voice.Silence_ChargeJester = clip; break;
            case "Abomination_SCP682": Voice.Abomination_EngageForestGiant = clip; break;
            case "Disgrace_SCP682": Voice.Disgrace_EngageEyelessDog = clip; break;
            case "Pathetic_SCP682": Voice.Pathetic_HitByPlayerFirstTime = clip; break;
            case "LoathsomeParasites_SCP682": Voice.LoathsomeParasites_MultiplePlayersAttacking = clip; break;
            // case "Worms_SCP682": Voice.FullRant_UponRevival = clip; break; // Didn't find clip
            case "PerversionOfExistence_SCP682": Voice.PerversionOfExistence_Flamingos = clip; break;
            case "TearYouApart_SCP682": Voice.TearYouApart_DraggingPlayer = clip; break;
            case "Disgusting_SCP682": Voice.Disgusting_KilledPlayer = clip; break;
            case "Useless_SCP682": Voice.Useless_ChasingPlayerForSomeTime = clip; break;
            case "Cowards_SCP682": Voice.Cowards_LostPlayer = clip; break;
            default: Plugin.Logger.LogError($"AudioClip with name '{clip.name}' was not identified!"); break;
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
    public static List<AudioClip> spawn = [];

    public static class Voice
    {
        public static AudioClip Worms_EngageIndoorEnemies = null!;
        public static AudioClip Bothersome_EngageBaboonHawk = null!;
        public static AudioClip Silence_ChargeJester = null!;
        public static AudioClip Abomination_EngageForestGiant = null!;
        public static AudioClip Disgrace_EngageEyelessDog = null!;
        public static AudioClip Pathetic_HitByPlayerFirstTime = null!;
        public static AudioClip LoathsomeParasites_MultiplePlayersAttacking = null!;
        public static AudioClip FullRant_UponRevival = null!;
        public static AudioClip PerversionOfExistence_Flamingos = null!;
        public static AudioClip TearYouApart_DraggingPlayer = null!;
        public static AudioClip Disgusting_KilledPlayer = null!;
        public static AudioClip Useless_ChasingPlayerForSomeTime = null!;
        public static AudioClip Cowards_LostPlayer = null!;
    }

    public static AudioClip FromRandom(this List<AudioClip> clips, System.Random random)
        => clips[random.Next(clips.Count)];
}