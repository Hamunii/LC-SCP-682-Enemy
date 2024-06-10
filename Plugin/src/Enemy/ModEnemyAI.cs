/*
 * https://github.com/Skull220/Welcome_To_Ooblterra/blob/master/Enemies/WTOEnemy.cs
 * Copyright (c) 2023 Skull
 * Skull has given the permission to use this file for the base of our Mod AI class
 * This class has been modified, and is licensed under the MIT license
*/

using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.SCPEnemy;

// Heavily based on WelcomeToOoblterra's WTOEnemy class
public class ModEnemyAI<T> : EnemyAI
    where T : EnemyAI
{
    public abstract class AIBehaviorState
    {
        public Vector2 RandomRange = new Vector2(0, 0);
        public int MyRandomInt = 0;
        public T self = default!;
        public NavMeshAgent agent = null!;
        public System.Random enemyRandom = null!;
        public abstract void OnStateEntered(Animator creatureAnimator);

        public virtual void UpdateBehavior(Animator creatureAnimator) { }

        public virtual void AIInterval(Animator creatureAnimator) { }

        public abstract void OnStateExit(Animator creatureAnimator);

        public abstract List<AIStateTransition> Transitions { get; set; }
    }

    public abstract class AIStateTransition
    {
        //public int enemyIndex { get; set; }
        public T self = default!;
        public abstract bool CanTransitionBeTaken();
        public abstract AIBehaviorState NextState();
    }

    public enum PlayerState
    {
        Dead,
        Outside,
        Inside,
        Ship
    }

    internal AIBehaviorState InitialState = null!;
    internal AIBehaviorState ActiveState = null!;
    internal System.Random enemyRandom = null!;
    internal float AITimer;
    internal bool PrintDebugs = false;
    internal PlayerState MyValidState = PlayerState.Inside;
    internal AIStateTransition nextTransition = null!;
    internal List<AIStateTransition> GlobalTransitions = new List<AIStateTransition>();
    internal List<AIStateTransition> AllTransitions = new List<AIStateTransition>();
    internal T self = default!;

    public override string __getTypeName()
    {
        return GetType().Name;
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();
        ActiveState.AIInterval(creatureAnimator);
    }

    public override void Start()
    {
        base.Start();

        //Initializers
        ActiveState = InitialState;
        enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
        if (enemyType.isOutsideEnemy)
        {
            MyValidState = PlayerState.Outside;
        }
        else
        {
            MyValidState = PlayerState.Inside;
        }
        //Debug to make sure that the agent is actually on the NavMesh
        if (!agent.isOnNavMesh && base.IsOwner)
        {
            LogDebug(
                "CREATURE " + this.__getTypeName() + " WAS NOT PLACED ON NAVMESH, DESTROYING..."
            );
            KillEnemyOnOwnerClient();
        }
        //Fix for the animator sometimes deciding to just not work
        creatureAnimator.Rebind();
        ActiveState.self = self;
        ActiveState.agent = agent;
        ActiveState.enemyRandom = enemyRandom;
        ActiveState.OnStateEntered(creatureAnimator);
    }

    public override void Update()
    {
        if (isEnemyDead)
        {
            return;
        }
        base.Update();
        AITimer += Time.deltaTime;
        //don't run enemy ai if they're dead

        bool RunUpdate = true;

        //Reset transition list to match all those in our current state, along with any global transitions that exist regardless of state (stunned, mostly)
        AllTransitions.Clear();
        AllTransitions.AddRange(GlobalTransitions);
        AllTransitions.AddRange(ActiveState.Transitions);

        foreach (AIStateTransition TransitionToCheck in AllTransitions)
        {
            TransitionToCheck.self = self;
            if (TransitionToCheck.CanTransitionBeTaken() && base.IsOwner)
            {
                RunUpdate = false;
                nextTransition = TransitionToCheck;
                TransitionStateServerRpc(
                    nextTransition.ToString(),
                    GenerateNextRandomInt(nextTransition.NextState().RandomRange)
                );
                return;
            }
        }

        if (RunUpdate)
        {
            ActiveState.UpdateBehavior(creatureAnimator);
        }
    }

    internal void LogDebug(object data)
    {
        if (PrintDebugs)
        {
            P.Log(data);
        }
    }

    internal bool PlayerCanBeTargeted(PlayerControllerB myPlayer)
    {
        return GetPlayerState(myPlayer) == MyValidState;
    }

    internal PlayerState GetPlayerState(PlayerControllerB myPlayer)
    {
        if (myPlayer.isPlayerDead)
        {
            return PlayerState.Dead;
        }
        if (myPlayer.isInsideFactory)
        {
            return PlayerState.Inside;
        }
        if (myPlayer.isInHangarShipRoom)
        {
            return PlayerState.Ship;
        }
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

    internal void OverrideState(AIBehaviorState state)
    {
        if (isEnemyDead)
        {
            return;
        }
        ActiveState = state;
        ActiveState.self = self;
        ActiveState.agent = agent;
        ActiveState.enemyRandom = enemyRandom;
        ActiveState.OnStateEntered(creatureAnimator);
        return;
    }

    public PlayerControllerB? IsAnyPlayerWithinLOS(
        int range = 45,
        float width = 60,
        int proximityAwareness = -1,
        bool DoLinecast = true,
        bool PrintResults = false,
        bool SortByDistance = false
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
            if (
                IsTargetPlayerWithinLOS(
                    Player,
                    range,
                    width,
                    proximityAwareness,
                    DoLinecast,
                    PrintResults
                )
            )
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
        PlayerControllerB player,
        int range = 45,
        float width = 60,
        int proximityAwareness = -1,
        bool DoLinecast = true,
        bool PrintResults = false
    )
    {
        float DistanceToTarget = Vector3.Distance(
            transform.position,
            player.gameplayCamera.transform.position
        );
        bool TargetInDistance = DistanceToTarget < (float)range;
        float AngleToTarget = Vector3.Angle(
            eye.transform.forward,
            player.gameplayCamera.transform.position - eye.transform.position
        );
        bool TargetWithinViewCone = AngleToTarget < width;
        bool TargetWithinProxAwareness = DistanceToTarget < proximityAwareness;
        bool LOSBlocked =
            DoLinecast
            && Physics.Linecast(
                eye.transform.position,
                player.transform.position,
                StartOfRound.Instance.collidersRoomDefaultAndFoliage,
                QueryTriggerInteraction.Ignore
            );
        if (PrintResults)
        {
            LogDebug(
                $"Target in Distance: {TargetInDistance} ({DistanceToTarget})"
                    + $"Target within view cone: {TargetWithinViewCone} ({AngleToTarget})"
                    + $"LOSBlocked: {LOSBlocked}"
            );
        }
        return (TargetInDistance && TargetWithinViewCone)
            || TargetWithinProxAwareness && !LOSBlocked;
    }

    public bool IsTargetPlayerWithinLOS(
        int range = 45,
        float width = 60,
        int proximityAwareness = -1,
        bool DoLinecast = true,
        bool PrintResults = false
    )
    {
        if (targetPlayer == null)
        {
            LogDebug(
                $"{this.__getTypeName()} called Target Player LOS check called with null target player; returning false!"
            );
            return false;
        }
        return IsTargetPlayerWithinLOS(
            targetPlayer,
            range,
            width,
            proximityAwareness,
            DoLinecast,
            PrintResults
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
            LogDebug($"There is somehow no closest player. get fucked");
            return null!;
        }

        return Result;
    }

    internal bool IsPlayerReachable()
    {
        if (targetPlayer == null)
        {
            P.LogError("Player Reach Test has no target player or passed in argument!");
            return false;
        }
        return IsPlayerReachable(targetPlayer);
    }

    internal bool IsPlayerReachable(PlayerControllerB PlayerToCheck)
    {
        Vector3 Position = RoundManager.Instance.GetNavMeshPosition(
            targetPlayer.transform.position,
            RoundManager.Instance.navHit,
            2.7f
        );
        if (!RoundManager.Instance.GotNavMeshPositionResult)
        {
            LogDebug("Player Reach Test: No NavMesh position");
            return false;
        }
        agent.CalculatePath(Position, agent.path);
        bool HasPath = agent.path.status == NavMeshPathStatus.PathComplete;
        LogDebug($"Player Reach Test: {HasPath}");
        return HasPath;
    }

    internal float PlayerDistanceFromShip(PlayerControllerB? PlayerToCheck = null)
    {
        if (PlayerToCheck == null)
        {
            if (targetPlayer == null)
            {
                P.LogError("PlayerNearShip check has no target player or passed in argument!");
                return -1;
            }
            PlayerToCheck = targetPlayer;
        }
        float DistanceFromShip = Vector3.Distance(
            targetPlayer.transform.position,
            StartOfRound.Instance.shipBounds.transform.position
        );
        LogDebug($"PlayerNearShip check: {DistanceFromShip}");
        return DistanceFromShip;
    }

    internal bool PlayerWithinRange(float Range, bool IncludeYAxis = true)
    {
        LogDebug($"Distance from target player: {DistanceFromTargetPlayer(IncludeYAxis)}");
        return DistanceFromTargetPlayer(IncludeYAxis) <= Range;
    }

    internal bool PlayerWithinRange(PlayerControllerB player, float Range, bool IncludeYAxis = true)
    {
        return DistanceFromTargetPlayer(player, IncludeYAxis) <= Range;
    }

    private float DistanceFromTargetPlayer(bool IncludeYAxis)
    {
        if (targetPlayer == null)
        {
            P.LogError(
                $"{this} attempted DistanceFromTargetPlayer with null target; returning -1!"
            );
            return -1f;
        }
        if (IncludeYAxis)
        {
            return Vector3.Distance(targetPlayer.transform.position, this.transform.position);
        }
        Vector2 PlayerFlatLocation = new Vector2(
            targetPlayer.transform.position.x,
            targetPlayer.transform.position.z
        );
        Vector2 EnemyFlatLocation = new Vector2(transform.position.x, transform.position.z);
        return Vector2.Distance(PlayerFlatLocation, EnemyFlatLocation);
    }

    private float DistanceFromTargetPlayer(PlayerControllerB player, bool IncludeYAxis)
    {
        if (IncludeYAxis)
        {
            return Vector3.Distance(player.transform.position, this.transform.position);
        }
        Vector2 PlayerFlatLocation = new Vector2(
            targetPlayer.transform.position.x,
            targetPlayer.transform.position.z
        );
        Vector2 EnemyFlatLocation = new Vector2(transform.position.x, transform.position.z);
        return Vector2.Distance(PlayerFlatLocation, EnemyFlatLocation);
    }

    internal bool AnimationIsFinished(string AnimName)
    {
        if (!creatureAnimator.GetCurrentAnimatorStateInfo(0).IsName(AnimName))
        {
            LogDebug(
                __getTypeName()
                    + ": Checking for animation "
                    + AnimName
                    + ", but current animation is "
                    + creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name
            );
            return true;
        }
        return creatureAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f;
    }

    internal int GenerateNextRandomInt(Vector2 Range)
    {
        Range = nextTransition.NextState().RandomRange;
        return enemyRandom.Next((int)Range.x, (int)Range.y);
    }

    [ServerRpc(RequireOwnership = false)]
    internal void SetAnimTriggerOnServerRpc(string name)
    {
        if (IsServer)
        {
            creatureAnimator.SetTrigger(name);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    internal void SetAnimBoolOnServerRpc(string name, bool state)
    {
        if (IsServer)
        {
            creatureAnimator.SetBool(name, state);
        }
    }

    [ServerRpc]
    internal void TransitionStateServerRpc(string StateName, int RandomInt)
    {
        TransitionStateClientRpc(StateName, RandomInt);
    }

    [ClientRpc]
    internal void TransitionStateClientRpc(string StateName, int RandomInt)
    {
        TransitionState(StateName, RandomInt);
    }

    internal void TransitionState(string StateName, int RandomInt)
    {
        //Jesus fuck I can't believe I have to do this
        Type type = Type.GetType(StateName);
        AIStateTransition LocalNextTransition = (AIStateTransition)Activator.CreateInstance(type);
        LocalNextTransition.self = self;
        if (LocalNextTransition.NextState().GetType() == ActiveState.GetType())
        {
            return;
        }
        //LogMessage(StateName);
        LogDebug($"{__getTypeName()} #{self} is Exiting:  {ActiveState}");
        ActiveState.OnStateExit(creatureAnimator);
        LogDebug($"{__getTypeName()} #{self} is Transitioning via:  {LocalNextTransition}");
        ActiveState = LocalNextTransition.NextState();
        ActiveState.MyRandomInt = RandomInt;
        ActiveState.self = self;
        ActiveState.agent = agent;
        ActiveState.enemyRandom = enemyRandom;
        LogDebug($"{__getTypeName()} #{self} is Entering:  {ActiveState}");
        ActiveState.OnStateEntered(creatureAnimator);

        //Debug Prints
        StartOfRound.Instance.ClientPlayerList.TryGetValue(
            NetworkManager.Singleton.LocalClientId,
            out var value
        );
        LogDebug(
            $"CREATURE: {enemyType.name} #{self} STATE: {ActiveState} ON PLAYER: #{value} ({StartOfRound.Instance.allPlayerScripts[value].playerUsername})"
        );
    }

    [ServerRpc]
    internal void SetTargetServerRpc(int PlayerID)
    {
        SetTargetClientRpc(PlayerID);
    }

    [ClientRpc]
    internal void SetTargetClientRpc(int PlayerID)
    {
        if (PlayerID == -1)
        {
            targetPlayer = null;
            LogDebug($"Clearing target on {this}");
            return;
        }
        if (StartOfRound.Instance.allPlayerScripts[PlayerID] == null)
        {
            LogDebug($"Index invalid! {this}");
            return;
        }
        targetPlayer = StartOfRound.Instance.allPlayerScripts[PlayerID];
        LogDebug($"{this} setting target to: {targetPlayer.playerUsername}");
    }
}
