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
    where T : ModEnemyAI<T>
{
    public abstract class AIBehaviorState
    {
        public T self = default!;
        public NavMeshAgent agent = null!;
        public System.Random enemyRandom = null!;
        public Animator creatureAnimator = null!;
        public abstract void OnStateEntered();

        /// <summary>Runs every frame.</summary>
        public virtual void UpdateBehavior() { }

        /// <summary>Runs at <c>DoAIInterval</c>, which the interval depends on EnemyAI's <c>AIIntervalTime</c>.</summary>
        public virtual void AIInterval() { }

        public abstract void OnStateExit();

        /// <summary>All the transitions that can be made from current State, excluding global transitions.</summary>
        public abstract List<AIStateTransition> Transitions { get; set; }
    }

    public abstract class AIStateTransition
    {
        public T self = default!;
        public NavMeshAgent agent = null!;
        public System.Random enemyRandom = null!;
        public Animator creatureAnimator = null!;
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
        ActiveState.AIInterval();
    }

    public override void Start()
    {
        base.Start();

        //Initializers
        ActiveState = InitialState;
        enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);

        if (enemyType.isOutsideEnemy)
            MyValidState = PlayerState.Outside;
        else
            MyValidState = PlayerState.Inside;

        //Debug to make sure that the agent is actually on the NavMesh
        if (!agent.isOnNavMesh && base.IsOwner)
        {
            DebugLog("CREATURE " + __getTypeName() + " WAS NOT PLACED ON NAVMESH, DESTROYING...");
            KillEnemyOnOwnerClient();
        }
        //Fix for the animator sometimes deciding to just not work
        creatureAnimator.Rebind();
        InitializeState(ActiveState, self, enemyRandom);
    }

    private void InitializeState(AIBehaviorState ActiveState, T self, System.Random enemyRandom)
    {
        ActiveState.self = self;
        ActiveState.agent = self.agent;
        ActiveState.enemyRandom = enemyRandom;
        ActiveState.creatureAnimator = self.creatureAnimator;
        ActiveState.OnStateEntered();
    }

    private void InitializeStateTransition(AIStateTransition transition, T self)
    {
        transition.self = self;
        transition.agent = self.agent;
        transition.enemyRandom = self.enemyRandom;
        transition.creatureAnimator = self.creatureAnimator;
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
            InitializeStateTransition(TransitionToCheck, self);
            if (TransitionToCheck.CanTransitionBeTaken() && base.IsOwner)
            {
                RunUpdate = false;
                nextTransition = TransitionToCheck;
                TransitionStateServerRpc(nextTransition.ToString());
                return;
            }
        }

        if (RunUpdate)
        {
            ActiveState.UpdateBehavior();
        }
    }

    internal void DebugLog(object data)
    {
        if (PrintDebugs)
            P.Log(data);
    }

    internal bool AnimationIsFinished(string AnimName)
    {
        if (!creatureAnimator.GetCurrentAnimatorStateInfo(0).IsName(AnimName))
        {
            DebugLog(
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

    internal void OverrideState(AIBehaviorState state)
    {
        if (isEnemyDead)
            return;

        TransitionStateServerRpc(state.ToString());
    }

    [ServerRpc]
    internal void TransitionStateServerRpc(string stateName) =>
        TransitionStateClientRpc(stateName, enemyRandom.Next());

    [ClientRpc]
    internal void TransitionStateClientRpc(string stateName, int randomSeed) =>
        TransitionState(stateName, randomSeed);

    internal void TransitionState(string stateName, int randomSeed)
    {
        //Jesus fuck I can't believe I have to do this
        Type type = Type.GetType(stateName);
        AIStateTransition localNextTransition = (AIStateTransition)Activator.CreateInstance(type);
        InitializeStateTransition(localNextTransition, self);

        if (localNextTransition.NextState().GetType() == ActiveState.GetType())
            return;

        //LogMessage(stateName);
        DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Exiting:  {ActiveState}");
        ActiveState.OnStateExit();
        DebugLog(
            $"{__getTypeName()} #{self.thisEnemyIndex} is Transitioning via:  {localNextTransition}"
        );
        ActiveState = localNextTransition.NextState();
        DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Entering:  {ActiveState}");
        InitializeState(ActiveState, self, new(randomSeed));

        //Debug Prints
        StartOfRound.Instance.ClientPlayerList.TryGetValue(
            NetworkManager.Singleton.LocalClientId,
            out var value
        );
        DebugLog(
            $"CREATURE: {enemyType.name} #{self.thisEnemyIndex} STATE: {ActiveState} ON PLAYER: #{value} ({StartOfRound.Instance.allPlayerScripts[value].playerUsername})"
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
            DebugLog($"Clearing target on {this}");
            return;
        }
        if (StartOfRound.Instance.allPlayerScripts[PlayerID] == null)
        {
            DebugLog($"Index invalid! {this}");
            return;
        }
        targetPlayer = StartOfRound.Instance.allPlayerScripts[PlayerID];
        _targetPlayerForOwner = targetPlayer;
        DebugLog($"{this} setting target to: {targetPlayer.playerUsername}");
    }
}
