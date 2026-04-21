
using System;
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
            }
        }
    }

    private static void InitializeVoiceLine(AudioClip clip)
    {
        switch (clip.name)
        {
            case "Worms_SCP682": Voice.Worms_EngageIndoorEnemies = clip; break;
            case "Bothersome_SPC682": Voice.Bothersome_EngageBaboonHawk = clip; break;
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
            case "GrowlyPurr_SCP682": /* TODO: Unused clip. Where should it be used? */ break;
            default: InitializeDoorBash(clip); break;
        }
    }

    private static void InitializeDoorBash(AudioClip clip)
    {
        switch (clip.name.Split("SFX")[0])
        {
            case "bash": DoorBash.BashSFX.Add(clip); break;
            case "metalDoorSmash": DoorBash.MetalDoorSmashSFX.Add(clip); break;
            case "doorWoosh": DoorBash.DoorWooshSFX.Add(clip); break;
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
        public static AudioClip Bothersome_EngageBaboonHawk = null!; // TODO: find clip
        public static AudioClip Silence_ChargeJester = null!;
        public static AudioClip Abomination_EngageForestGiant = null!;
        public static AudioClip Disgrace_EngageEyelessDog = null!;
        public static AudioClip Pathetic_HitByPlayerFirstTime = null!;
        public static AudioClip LoathsomeParasites_MultiplePlayersAttacking = null!;
        public static AudioClip FullRant_UponRevival = null!; // TODO: find clip
        public static AudioClip PerversionOfExistence_Flamingos = null!; // TODO: do this
        public static AudioClip TearYouApart_DraggingPlayer = null!;
        public static AudioClip Disgusting_KilledPlayer = null!;
        public static AudioClip Useless_ChasingPlayerForSomeTime = null!;
        public static AudioClip Cowards_LostPlayer = null!;

        public static AudioClip GetClip(VoiceCode clip) => clip switch
        {
            VoiceCode.Worms_EngageIndoorEnemies => Worms_EngageIndoorEnemies,
            VoiceCode.Bothersome_EngageBaboonHawk => Bothersome_EngageBaboonHawk,
            VoiceCode.Silence_ChargeJester => Silence_ChargeJester,
            VoiceCode.Abomination_EngageForestGiant => Abomination_EngageForestGiant,
            VoiceCode.Disgrace_EngageEyelessDog => Disgrace_EngageEyelessDog,
            VoiceCode.Pathetic_HitByPlayerFirstTime => Pathetic_HitByPlayerFirstTime,
            VoiceCode.LoathsomeParasites_MultiplePlayersAttacking => LoathsomeParasites_MultiplePlayersAttacking,
            VoiceCode.FullRant_UponRevival => FullRant_UponRevival,
            VoiceCode.PerversionOfExistence_Flamingos => PerversionOfExistence_Flamingos,
            VoiceCode.TearYouApart_DraggingPlayer => TearYouApart_DraggingPlayer,
            VoiceCode.Disgusting_KilledPlayer => Disgusting_KilledPlayer,
            VoiceCode.Useless_ChasingPlayerForSomeTime => Useless_ChasingPlayerForSomeTime,
            VoiceCode.Cowards_LostPlayer => Cowards_LostPlayer,
            _ => throw new InvalidOperationException($"A voice clip with code {clip} doesn't exist.")
        };
    }

    public enum VoiceCode
    {
        Worms_EngageIndoorEnemies,
        Bothersome_EngageBaboonHawk,
        Silence_ChargeJester,
        Abomination_EngageForestGiant,
        Disgrace_EngageEyelessDog,
        Pathetic_HitByPlayerFirstTime,
        LoathsomeParasites_MultiplePlayersAttacking,
        FullRant_UponRevival,
        PerversionOfExistence_Flamingos,
        TearYouApart_DraggingPlayer,
        Disgusting_KilledPlayer,
        Useless_ChasingPlayerForSomeTime,
        Cowards_LostPlayer,
    }

    public static class DoorBash
    {
        public static List<AudioClip> BashSFX { get; private set; } = [];
        public static List<AudioClip> MetalDoorSmashSFX { get; private set; } = [];
        public static List<AudioClip> DoorWooshSFX { get; private set; } = [];
    }

    public static AudioClip FromRandom(this List<AudioClip> clips, System.Random random)
        => clips[random.Next(clips.Count)];
}