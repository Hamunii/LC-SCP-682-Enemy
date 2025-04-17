using System;
using System.Collections;
using MonoMod.Cil;
using SCP682.SCPEnemy;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.Hooks;

static class ApparatusTakenHook
{
    internal static void Init()
    {
        On.LungProp.DisconnectFromMachinery += LungProp_DisconnectFromMachinery;

        // This method calls DisconnectFromMachinery, but it might get inlined
        // if another mod hooks this method, so we force a recompilation for this method
        // so it will reference our hook we just applied.
        IL.LungProp.EquipItem += LungProp_EquipItem;
    }

    private static void LungProp_EquipItem(ILContext il)
    {
        return;
    }

    private static IEnumerator LungProp_DisconnectFromMachinery(On.LungProp.orig_DisconnectFromMachinery orig, LungProp self)
    {
        var origIEnumerator = orig(self);

        while (origIEnumerator.MoveNext())
            yield return origIEnumerator.Current;

        if (UnityEngine.Random.Range(0, 2) == 0)
            SpawnSCP682(true);
        else
            SpawnSCP682(false);
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

        Vector3 position = spawnPoints[roundManager.AnomalyRandom.Next(0, spawnPoints.Length)].transform.position;

        position = roundManager.GetRandomNavMeshPositionInBoxPredictable(
            position, 10f, default, roundManager.AnomalyRandom,
            roundManager.GetLayermaskForEnemySizeLimit(Plugin.SCP682ET));

        position = roundManager.PositionWithDenialPointsChecked(position, spawnPoints, Plugin.SCP682ET);

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
