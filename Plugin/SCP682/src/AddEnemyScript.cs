﻿using SCP682.SCPEnemy;
using SCP682.SCPEnemy.DoorBreak;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682;

static class AddEnemyScript
{
    internal static void SCP682AI(EnemyType enemyType, AssetBundle assets)
    {
        var pref = enemyType.enemyPrefab;
        var ai = pref.AddComponent<SCP682AI>();

        ai.enemyType = enemyType;

        ai.creatureAnimator = pref.GetComponentInChildren<Animator>();
        ai.creatureVoice = pref.transform.Find("ModelRoot/CrocodileModel/CG/Pelvis/CreatureVoice").GetComponent<AudioSource>();
        ai.creatureSFX = pref.transform.Find("CreatureSFX").GetComponent<AudioSource>();
        ai.eye = pref.transform.Find("Eye");
        ai.dieSFX = assets.LoadAsset<AudioClip>("SkibidiDeath");

        ai.enemyBehaviourStates = new EnemyBehaviourState[20]; // These just need to exist to avoid index out of range

        // AI Calculation / Netcode
        ai.AIIntervalTime = 0.2f;
        ai.agent = pref.GetComponent<NavMeshAgent>();
        ai.updatePositionThreshold = 0.1f;
        ai.syncMovementSpeed = 0.22f;
        ai.enemyHP = 18;

        // Other
        var collisionDetect = pref.GetComponentInChildren<EnemyAICollisionDetect>();
        collisionDetect.mainScript = ai;
        var doorDestroyer = collisionDetect.gameObject.AddComponent<DoorDestroyerCollider>();
        doorDestroyer.AI = ai;
    }

    internal static void RemoveComponent<T>(this GameObject gameObject)
        where T : Component
    {
        var script = gameObject.GetComponent<T>();
        Object.Destroy(script);
    }
}
