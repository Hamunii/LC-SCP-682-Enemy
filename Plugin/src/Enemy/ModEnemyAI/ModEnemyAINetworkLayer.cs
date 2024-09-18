using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.SCPEnemy;

/// <summary>
/// A terrible horrible workaround for not being able to have Rpc methods in generic classes.
/// </summary>
public abstract partial class ModEnemyAINetworkLayer : EnemyAI
{
    internal Coroutine? _transitionCoroutineInProgress = null;

    [ServerRpc]
    internal void TransitionStateServerRpc(string stateName, int randomSeed) =>
        TransitionStateClientRpc(stateName, randomSeed);

    [ClientRpc]
    internal void TransitionStateClientRpc(string stateName, int randomSeed) =>
        _transitionCoroutineInProgress = StartCoroutine(TransitionState(stateName, randomSeed));

    internal abstract IEnumerator TransitionState(string stateOrTransitionName, int randomSeed);

    [ServerRpc]
    protected void SetTargetServerRpc(int PlayerID) =>
        SetTargetClientRpc(PlayerID);

    [ClientRpc]
    private void SetTargetClientRpc(int PlayerID) =>
        SetTarget(PlayerID);

    protected abstract void SetTarget(int PlayerID);

    [ServerRpc(RequireOwnership = false)]
    internal void SetAnimTriggerOnServerRpc(string name)
    {
        if (IsServer)
            creatureAnimator.SetTrigger(name);
    }

    [ServerRpc(RequireOwnership = false)]
    internal void SetAnimBoolOnServerRpc(string name, bool state)
    {
        if (IsServer)
            creatureAnimator.SetBool(name, state);
    }

    [ClientRpc]
    public void TeleportSelfToOtherEntranceClientRpc(bool isOutside) =>
        TeleportSelfToOtherEntrance(isOutside);

    protected abstract void TeleportSelfToOtherEntrance(bool isOutside);
}
