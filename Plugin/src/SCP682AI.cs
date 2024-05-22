using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace SCP682;

class SCP682AI : EnemyAI
{
    // We use this list to destroy loaded game objects when plugin is reloaded
    internal static List<GameObject> SCP682Objects = new();

    // We set these in our Asset Bundle, so we can disable warning CS0649:
    // Field 'field' is never assigned to, and will always have its default value 'value'
    #pragma warning disable 0649
    public Transform turnCompass = null!;
    public Transform attackArea = null!;
    #pragma warning restore 0649
    float timeSinceHittingLocalPlayer;
    float timeSinceNewRandPos;
    Vector3 positionRandomness;
    Vector3 StalkPos;
    System.Random enemyRandom = null!;
    bool isDeadAnimationDone;
    enum State {
        SearchingForPlayer,
        StickingInFrontOfPlayer,
        HeadSwingAttackInProgress,
    }

    [Conditional("DEBUG")]
    void LogIfDebugBuild(string text) {
        Plugin.Logger.LogInfo(text);
    }

    static class Anim
    {
        // do: trigger
        // is: boolean
        internal const string doKillEnemy = "KillEnemy";
        internal const string isMoving = "isMoving";
        internal const string isMovingInverted = "isMovingInverted";
        internal const string isRunning = "isRunning";
        internal const string isOnShip = "isOnShip";
        internal const string doBite = "doBite";
    }

    public override void Start() {
        enemyType.isOutsideEnemy = !GameNetworkManager.Instance.localPlayerController.isInsideFactory;
        base.Start();
        if(enemyType.isOutsideEnemy)
        {
            var scale = 4f;
            gameObject.transform.Find("CrocodileModel").localScale = new(scale, scale, scale);
        }
        LogIfDebugBuild("SCP-682 Spawned");
        SCP682Objects.Add(gameObject);
        timeSinceHittingLocalPlayer = 0;
        creatureAnimator.SetBool(Anim.isMoving, true);
        timeSinceNewRandPos = 0;
        positionRandomness = new Vector3(0, 0, 0);
        enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
        isDeadAnimationDone = false;
        // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
        // like a voice clip or an sfx clip to play when changing to that specific behavior state.
        currentBehaviourStateIndex = (int)State.SearchingForPlayer;
        // We make the enemy start searching. This will make it start wandering around.
        StartSearch(transform.position);
    }

    public override void Update() {
        base.Update();
        if(isEnemyDead){
            // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
            if(!isDeadAnimationDone){ 
                LogIfDebugBuild("Stopping enemy voice with janky code.");
                isDeadAnimationDone = true;
                creatureVoice.Stop();
                creatureVoice.PlayOneShot(dieSFX);
            }
            return;
        }
        timeSinceHittingLocalPlayer += Time.deltaTime;
        timeSinceNewRandPos += Time.deltaTime;
        
        var state = currentBehaviourStateIndex;
        if(targetPlayer != null && (state == (int)State.StickingInFrontOfPlayer || state == (int)State.HeadSwingAttackInProgress)){
            turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
        }
        if (stunNormalizedTimer > 0f)
        {
            agent.speed = 0f;
        }
    }

    public override void DoAIInterval() {
        
        base.DoAIInterval();
        if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
            return;
        };

        switch(currentBehaviourStateIndex) {
            case (int)State.SearchingForPlayer:
                agent.speed = 3f;
                if (FoundClosestPlayerInRange(25f, 3f)){
                    LogIfDebugBuild("Start Target Player");
                    StopSearch(currentSearch);
                    DoAnimBoolClientRpc(Anim.isRunning, true);
                    SwitchToBehaviourClientRpc((int)State.StickingInFrontOfPlayer);
                }
                break;

            case (int)State.StickingInFrontOfPlayer:
                agent.speed = 9f;
                // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 20 && !CheckLineOfSightForPosition(targetPlayer.transform.position))){
                    LogIfDebugBuild("Stop Target Player");
                    StartSearch(transform.position);
                    DoAnimBoolClientRpc(Anim.isRunning, false);
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    return;
                }
                StickingInFrontOfPlayer();
                break;

            case (int)State.HeadSwingAttackInProgress:
                // We don't care about doing anything here
                break;
                
            default:
                LogIfDebugBuild("This Behavior State doesn't exist!");
                break;
        }
    }

    bool FoundClosestPlayerInRange(float range, float senseRange) {
        TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
        if(targetPlayer == null){
            // Couldn't see a player, so we check if a player is in sensing distance instead
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
            range = senseRange;
        }
        return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
    }
    
    bool TargetClosestPlayerInAnyCase() {
        mostOptimalDistance = 2000f;
        targetPlayer = null;
        for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
        {
            tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
            if (tempDist < mostOptimalDistance)
            {
                mostOptimalDistance = tempDist;
                targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
            }
        }
        if(targetPlayer == null) return false;
        return true;
    }

    void StickingInFrontOfPlayer() {
        // We only run this method for the host because I'm paranoid about randomness not syncing I guess
        // This is fine because the game does sync the position of the enemy.
        // Also the attack is a ClientRpc so it should always sync
        if (targetPlayer == null || !IsOwner) {
            return;
        }
        if(timeSinceNewRandPos > 0.7f){
            timeSinceNewRandPos = 0;
            if(enemyRandom.Next(0, 8) == 0){
                // Attack
                DoAnimTriggerClientRpc(Anim.doBite);
            }
            else{
                // Go in front of player
                // positionRandomness = new Vector3(enemyRandom.Next(-1, 1), 0, enemyRandom.Next(-1, 1));
                StalkPos = targetPlayer.transform.position; // - Vector3.Scale(new Vector3(-1, 0, -1), targetPlayer.transform.forward) + positionRandomness;
            }
            SetDestinationToPosition(StalkPos, checkForPath: false);
        }
    }

    public override void OnCollideWithPlayer(Collider other) {
        if (timeSinceHittingLocalPlayer < 1f) {
            return;
        }
        PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
        if (playerControllerB != null)
        {
            LogIfDebugBuild("Example Enemy Collision with Player!");
            timeSinceHittingLocalPlayer = 0f;
            playerControllerB.DamagePlayer(20);
        }
    }

    public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
        base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        if(isEnemyDead){
            return;
        }
        enemyHP -= force;
        if (IsOwner) {
            if (enemyHP <= 0 && !isEnemyDead) {
                // Our death sound will be played through creatureVoice when KillEnemy() is called.
                // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                // so we don't need to call a death animation ourselves.

                // We need to stop our search coroutine, because the game does not do that by default.
                StopCoroutine(searchCoroutine);
                KillEnemyOnOwnerClient();
            }
        }
    }

    [ClientRpc]
    public void DoAnimBoolClientRpc(string animationName, bool value) {
        LogIfDebugBuild($"Animation: {animationName}");
        creatureAnimator.SetBool(animationName, value);
    }

    [ClientRpc]
    public void DoAnimTriggerClientRpc(string animationName) {
        LogIfDebugBuild($"Animation: {animationName}");
        creatureAnimator.SetTrigger(animationName);
    }

    [ClientRpc]
    public void SwingAttackHitClientRpc() {
        LogIfDebugBuild("SwingAttackHitClientRPC");
        int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity
        Collider[] hitColliders = Physics.OverlapBox(attackArea.position, attackArea.localScale, Quaternion.identity, playerLayer);
        if(hitColliders.Length > 0){
            foreach (var player in hitColliders){
                PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                if (playerControllerB != null)
                {
                    LogIfDebugBuild("Swing attack hit player!");
                    timeSinceHittingLocalPlayer = 0f;
                    playerControllerB.DamagePlayer(40);
                }
            }
        }
    }
}