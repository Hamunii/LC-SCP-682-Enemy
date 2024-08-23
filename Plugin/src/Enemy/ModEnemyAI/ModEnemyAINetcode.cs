
using GameNetcodeStuff;
using Unity.Netcode;

namespace SCP682.SCPEnemy;

public class ModEnemyAINetcode : NetworkBehaviour
{
    ModEnemyAINetcode Instance = null!;
    internal ModEnemyAI<SCP682AI> ModEnemyAI = null!;
    public PlayerControllerB? LastSyncedTargetPlayer;

    private void Awake()
    {
        Instance = this;
    }

    [ServerRpc]
    internal void TransitionStateServerRpc(string stateName) =>
        ModEnemyAI.TransitionStateServerRpcFromNetcode(stateName);

    [ClientRpc]
    internal void TransitionStateClientRpc(string stateName, int randomSeed) =>
        ModEnemyAI.TransitionStateClientRpcFromNetcode(stateName, randomSeed);

    [ServerRpc]
    internal void SetTargetServerRpc(int PlayerID)
    {
        SetTargetClientRpc(PlayerID);
    }

    [ClientRpc]
    internal void SetTargetClientRpc(int PlayerID)
    {
        ModEnemyAI.SetTargetClientRpcFromNetcode(PlayerID);
    }

    [ClientRpc]
    public void TeleportSelfToOtherEntranceClientRpc(bool isOutside)
    {
        ModEnemyAI.TeleportSelfToOtherEntranceClientRpcFromNetcode(isOutside);
    }
}