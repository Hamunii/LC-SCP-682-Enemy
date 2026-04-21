/*
 * This class is based on WTOEnemy from Welcome To Ooblterra:
 * https://github.com/Skeleton-Studios/Welcome_To_Ooblterra/blob/master/Enemies/WTOEnemy.cs
 *
 * Welcome To Ooblterra is licensed under the MIT license, which can be found here:
 * https://github.com/Skeleton-Studios/Welcome_To_Ooblterra/blob/master/LICENSE
 *
 * This class has been modified, and is licensed under the MIT license.
*/

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.SCPEnemy;

// Heavily based on WelcomeToOoblterra's WTOEnemy class
public abstract partial class ModEnemyAI<T> : ModEnemyAINetworkLayer
    where T : ModEnemyAI<T>
{
    internal bool AnimationIsFinished(string AnimName, int layerIndex)
    {
        if (!creatureAnimator.GetCurrentAnimatorStateInfo(layerIndex).IsName(AnimName))
        {
            DebugLog(
                __getTypeName()
                    + ": Checking for animation "
                    + AnimName
                    + ", but current animation is "
                    + creatureAnimator.GetCurrentAnimatorClipInfo(layerIndex)[0].clip.name
            );
            return true;
        }
        return creatureAnimator.GetCurrentAnimatorStateInfo(layerIndex).normalizedTime >= 1f;
    }

    internal bool PlayerCanBeTargeted(PlayerControllerB myPlayer) =>
        GetPlayerState(myPlayer) == myValidState;

    internal PlayerState GetPlayerState(PlayerControllerB myPlayer)
    {
        if (myPlayer.isPlayerDead)
            return PlayerState.Dead;

        if (myPlayer.isInsideFactory)
            return PlayerState.Inside;

        if (myPlayer.isInHangarShipRoom)
            return PlayerState.Ship;

        return PlayerState.Outside;
    }

    internal void MoveTimerValue(ref float Timer, bool ShouldRaise = false)
    {
        if (ShouldRaise)
        {
            Timer += Time.deltaTime;
            return;
        }
        if (Timer <= 0)
        {
            return;
        }
        Timer -= Time.deltaTime;
    }

    public IEnumerable<PlayerControllerB> AllPlayers()
    {
        foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
        {
            if (player.isPlayerDead || !player.isPlayerControlled)
                continue;

            yield return player;
        }
    }

    public PlayerControllerB? IsAnyPlayerWithinLOS(
        int range = 45, float width = 60,
        int proximityAwareness = -1, bool DoLinecast = true,
        bool PrintResults = false, bool SortByDistance = false
    )
    {
        float ShortestDistance = range;
        float NextDistance;
        PlayerControllerB? ClosestPlayer = null;
        foreach (PlayerControllerB Player in StartOfRound.Instance.allPlayerScripts)
        {
            if (Player.isPlayerDead || !Player.isPlayerControlled)
            {
                continue;
            }
            if (IsTargetPlayerWithinLOS(Player, range, width, proximityAwareness, DoLinecast, PrintResults))
            {
                if (!SortByDistance)
                {
                    return Player;
                }
                NextDistance = Vector3.Distance(Player.transform.position, this.transform.position);
                if (NextDistance < ShortestDistance)
                {
                    ShortestDistance = NextDistance;
                    ClosestPlayer = Player;
                }
            }
        }
        return ClosestPlayer;
    }

    public bool IsTargetPlayerWithinLOS(
        PlayerControllerB player, int range = 45, float width = 60,
        int proximityAwareness = -1, bool DoLinecast = true, bool PrintResults = false
    )
    {
        float DistanceToTarget = Vector3.Distance(transform.position, player.gameplayCamera.transform.position);
        bool TargetInDistance = DistanceToTarget < range;
        float AngleToTarget = Vector3.Angle(
            eye.transform.forward,
            player.gameplayCamera.transform.position - eye.transform.position
        );
        bool TargetWithinViewCone = AngleToTarget < width;
        bool TargetWithinProxAwareness = DistanceToTarget < proximityAwareness;
        bool LOSBlocked = DoLinecast && Physics.Linecast(
                eye.transform.position, player.transform.position,
                StartOfRound.Instance.collidersRoomDefaultAndFoliage, QueryTriggerInteraction.Ignore
            );
        if (PrintResults)
        {
            DebugLog(
                $"Target in Distance: {TargetInDistance} ({DistanceToTarget})"
                    + $"Target within view cone: {TargetWithinViewCone} ({AngleToTarget})"
                    + $"LOSBlocked: {LOSBlocked}"
            );
        }
        return (TargetInDistance && TargetWithinViewCone)
            || TargetWithinProxAwareness && !LOSBlocked;
    }

    public bool IsTargetPlayerWithinLOS(
        int range = 45, float width = 60,
        int proximityAwareness = -1, bool DoLinecast = true, bool PrintResults = false
    )
    {
        if (targetPlayer == null)
        {
            DebugLog(
                $"{this.__getTypeName()} called Target Player LOS check called with"
                + " null target player; returning false!"
            );
            return false;
        }
        return IsTargetPlayerWithinLOS(
            targetPlayer, range, width,
            proximityAwareness, DoLinecast, PrintResults
        );
    }

    public PlayerControllerB FindNearestPlayer(bool ValidateNav = false)
    {
        PlayerControllerB? Result = null;
        float BestDistance = 20000;
        for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
        {
            PlayerControllerB NextPlayer = StartOfRound.Instance.allPlayerScripts[i];
            if (ValidateNav && !agent.CalculatePath(NextPlayer.transform.position, path1))
            {
                continue;
            }
            float PlayerToMonster = Vector3.Distance(
                this.transform.position,
                NextPlayer.transform.position
            );
            if (PlayerToMonster < BestDistance)
            {
                Result = NextPlayer;
                BestDistance = PlayerToMonster;
            }
        }

        if (Result == null)
        {
            DebugLog($"There is somehow no closest player. get fucked");
            return null!;
        }

        return Result;
    }

    internal bool IsPlayerReachable()
    {
        if (targetPlayer == null)
        {
            PLog.LogError("Player Reach Test has no target player or passed in argument!");
            return false;
        }
        return IsPlayerReachable(targetPlayer);
    }

    internal bool IsPlayerReachable(PlayerControllerB? playerToCheck)
    {
        if (playerToCheck is null)
            return false;

        Vector3 Position = RoundManager.Instance.GetNavMeshPosition(
            playerToCheck.transform.position,
            RoundManager.Instance.navHit,
            2.7f
        );
        if (!RoundManager.Instance.GotNavMeshPositionResult)
        {
            DebugLog("Player Reach Test: No NavMesh position");
            return false;
        }
        agent.CalculatePath(Position, agent.path);
        bool HasPath = agent.path.status == NavMeshPathStatus.PathComplete;
        DebugLog($"Player Reach Test: {HasPath}");
        return HasPath;
    }

    internal float PlayerDistanceFromShip(PlayerControllerB playerToCheck)
    {
        if (playerToCheck is null)
        {
            PLog.LogError("PlayerNearShip check has no target player or passed in argument!");
            return -1;
        }
        float DistanceFromShip = Vector3.Distance(
            playerToCheck.transform.position,
            StartOfRound.Instance.shipBounds.transform.position
        );
        DebugLog($"PlayerNearShip check: {DistanceFromShip}");
        return DistanceFromShip;
    }

    internal bool PlayerWithinRange(PlayerControllerB player, float range, bool includeYAxis = true)
    {
        return DistanceFromPlayer(player, includeYAxis) <= range;
    }

    private float DistanceFromTargetPlayer(bool IncludeYAxis)
    {
        if (targetPlayer == null)
        {
            PLog.LogError($"{this} attempted DistanceFromTargetPlayer with null target; returning -1!");
            return -1f;
        }
        return DistanceFromPlayer(targetPlayer, IncludeYAxis);
    }

    private float DistanceFromPlayer(PlayerControllerB player, bool IncludeYAxis)
    {
        if (IncludeYAxis)
        {
            return Vector3.Distance(player.transform.position, this.transform.position);
        }
        Vector2 PlayerFlatLocation = new Vector2(player.transform.position.x, player.transform.position.z);
        Vector2 EnemyFlatLocation = new Vector2(transform.position.x, transform.position.z);
        return Vector2.Distance(PlayerFlatLocation, EnemyFlatLocation);
    }

    public new void SetEnemyOutside(bool toOutside)
    {
        if (toOutside)
        {
            myValidState = PlayerState.Outside;
            base.SetEnemyOutside(true);
        }
        else
        {
            myValidState = PlayerState.Inside;
            base.SetEnemyOutside(false);
        }
    }

    protected override void TeleportSelfToOtherEntrance(bool isOutside)
    {
        var targetEntrance = RoundManager.FindMainEntranceScript(!isOutside);

        Vector3 navMeshPosition = RoundManager.Instance.GetNavMeshPosition(
            targetEntrance.entrancePoint.position
        );
        if (IsOwner)
        {
            agent.enabled = false;
            transform.position = navMeshPosition;
            agent.enabled = true;
        }
        else
            transform.position = navMeshPosition;

        serverPosition = navMeshPosition;
        SetEnemyOutside(!isOutside);

        PlayEntranceOpeningSound(targetEntrance);
    }

    private void PlayEntranceOpeningSound(EntranceTeleport entrance)
    {
        if (entrance.doorAudios == null || entrance.doorAudios.Length == 0)
            return;
        entrance.entrancePointAudio.PlayOneShot(entrance.doorAudios[0]);
        WalkieTalkie.TransmitOneShotAudio(entrance.entrancePointAudio, entrance.doorAudios[0]);
    }

    internal void EnterSpecialAnimationWithPlayer(PlayerControllerB player, bool stopMovementCalculations = true)
    {
        if (player.inSpecialInteractAnimation && player.currentTriggerInAnimationWith != null)
            player.currentTriggerInAnimationWith.CancelAnimationExternally();

        player.inSpecialInteractAnimation = true;
        player.inAnimationWithEnemy = self;
        self.inSpecialAnimationWithPlayer = player;

        if (stopMovementCalculations)
            self.inSpecialAnimation = true;
    }

    /// <summary>
    /// A slightly modified version of <see cref="EnemyAI.MeetsStandardPlayerCollisionConditions(Collider, bool, bool)"/>
    /// </summary>
    /// <returns><see langword="true"/> if "other" is a valid player, otherwise <see langword="false"/>.</returns>
    internal bool TryGetValidPlayerFromCollision(Collider other, [NotNullWhen(returnValue: true)] out PlayerControllerB? player, bool allowNonLocalPlayer = false)
    {
        player = null;

        if (isEnemyDead)
            return false;

        if (!ventAnimationFinished)
            return false;

        if (stunNormalizedTimer >= 0f)
            return false;

        player = other.gameObject.GetComponent<PlayerControllerB>();
        if (player == null || (!allowNonLocalPlayer && player != GameNetworkManager.Instance.localPlayerController))
            return false;

        if (!PlayerIsTargetable(player, cannotBeInShip: false, overrideInsideFactoryCheck: false))
            return false;

        return true;
    }

    internal bool IsPlayerInsideCollider(PlayerControllerB? player, Collider collider, float colliderScale = 1f)
    {
        if (player == null)
            return false;

        int playerLayer = 1 << 3; // The player layer is the 3rd layer in the game, can be checked from Asset Ripper output.
        Collider[] colliders =
            Physics.OverlapBox(
                collider.bounds.center,
                collider.bounds.extents / 2 * colliderScale,
                Quaternion.identity,
                playerLayer);

        foreach (Collider collided in colliders)
        {
            if (!collided.CompareTag("Player"))
                continue;

            if (!TryGetValidPlayerFromCollision(collided, out var collidedPlayer, allowNonLocalPlayer: true))
                continue;

            if (collidedPlayer == player)
                return true;
        }
        return false;
    }
}
