using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.Hooks;

static class ApparatusTakenHook
{
    internal static void Init()
    {
        On.LungProp.DisconnectFromMachinery += LungProp_DisconnectFromMachinery;
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

    private static void SpawnSCP682(bool outside)
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

        roundManager.SpawnEnemyGameObject(position, 0, 1, Plugin.SCP682ET);
    }
}
