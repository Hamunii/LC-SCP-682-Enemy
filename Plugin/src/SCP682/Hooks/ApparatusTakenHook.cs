using System;
using System.Collections;
using MonoDetour.HookGen;
using MonoDetour.Reflection.Unspeakable;
using MonoMod.Cil;
using SCP682.SCPEnemy;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[assembly: MonoDetourTargets(typeof(LungProp), Members = ["DisconnectFromMachinery"])]

namespace SCP682.Hooks;

static class ApparatusTakenHook
{
    internal static void Init()
    {
        Md.LungProp.DisconnectFromMachinery.PostfixMoveNext(
            Postfix_LungProp_DisconnectFromMachinery_MoveNext
        );
    }

    private static void Postfix_LungProp_DisconnectFromMachinery_MoveNext(
        SpeakableEnumerator<object, LungProp> self,
        ref bool continueEnumeration
    )
    {
        if (continueEnumeration)
            return;

        if (UnityEngine.Random.Range(0, 2) == 0)
            SpawnSCP682(outside: true);
        else
            SpawnSCP682(outside: false);
    }

    internal static void SpawnSCP682(bool outside)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        if (Plugin.SCP682ET.numberSpawned > 0)
            return;

        var roundManager = RoundManager.Instance;
        GameObject[] spawnPoints;

        if (outside)
        {
            spawnPoints = GameObject.FindGameObjectsWithTag("OutsideAINode");
            roundManager.currentOutsideEnemyPower += Plugin.SCP682ET.PowerLevel;
        }
        else
        {
            spawnPoints = GameObject.FindGameObjectsWithTag("AINode");
            roundManager.currentEnemyPower += Plugin.SCP682ET.PowerLevel;
        }
        Plugin.SCP682ET.numberSpawned++;

        Vector3 position = spawnPoints[roundManager.AnomalyRandom.Next(0, spawnPoints.Length)]
            .transform
            .position;

        position = roundManager.GetRandomNavMeshPositionInBoxPredictable(
            position,
            10f,
            default,
            roundManager.AnomalyRandom,
            roundManager.GetLayermaskForEnemySizeLimit(Plugin.SCP682ET)
        );

        position = roundManager.PositionWithDenialPointsChecked(
            position,
            spawnPoints,
            Plugin.SCP682ET
        );

        var enemyNetObj = roundManager.SpawnEnemyGameObject(position, 0, 1, Plugin.SCP682ET);
        if (!enemyNetObj.TryGet(out var netObj))
        {
            Plugin.Logger.LogError("Couldn't get network object for spawned enemy!");
            return;
        }

        var ai = netObj.GetComponent<SCP682AI>();
        ai.enemyType.isOutsideEnemy = outside;
    }
}
