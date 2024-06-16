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
public partial class ModEnemyAI<T> : EnemyAI
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
    private PlayerControllerB? _targetPlayerForOwner;
    public new PlayerControllerB? targetPlayer
    {
        get
        {
            if (IsOwner && base.targetPlayer != _targetPlayerForOwner)
            {
                if (base.targetPlayer is not null)
                    SetTargetServerRpc((int)base.targetPlayer.actualClientId);
                else
                    SetTargetServerRpc(-1);
            }
            return base.targetPlayer;
        }
        set
        {
            if (value is not null)
                SetTargetServerRpc((int)value.actualClientId);
            else
                SetTargetServerRpc(-1);
        }
    }

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
    private void SetTargetServerRpc(int PlayerID)
    {
        SetTargetClientRpc(PlayerID);
    }

    [ClientRpc]
    private void SetTargetClientRpc(int PlayerID)
    {
        if (PlayerID == -1)
        {
            targetPlayer = null;
            _targetPlayerForOwner = null;
            LogDebug($"Clearing target on {this}");
            return;
        }
        if (StartOfRound.Instance.allPlayerScripts[PlayerID] == null)
        {
            LogDebug($"Index invalid! {this}");
            return;
        }
        targetPlayer = StartOfRound.Instance.allPlayerScripts[PlayerID];
        _targetPlayerForOwner = targetPlayer;
        LogDebug($"{this} setting target to: {targetPlayer.playerUsername}");
    }
}
