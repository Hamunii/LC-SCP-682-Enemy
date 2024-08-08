/*
 * https://github.com/Skull220/Welcome_To_Ooblterra/blob/master/Enemies/WTOEnemy.cs
 * Copyright (c) 2023 Skull
 * Skull has given the permission to use this file for the base of our Mod AI class
 * This class has been modified, and is licensed under the MIT license
*/

using System;
using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.SCPEnemy;

// Heavily based on WelcomeToOoblterra's WTOEnemy class
public abstract partial class ModEnemyAI<T> : EnemyAI
    where T : ModEnemyAI<T>
{
    public abstract class AIBehaviorState
    {
        public T self = default!;
        public NavMeshAgent agent = null!;
        public System.Random enemyRandom = null!;
        public Animator creatureAnimator = null!;
        public abstract IEnumerator OnStateEntered();

        /// <summary>Runs every frame, but never before OnStateEntered not after OnStateExit.</summary>
        public virtual void Update() { }
        /// <summary>Runs every frame after the normal Update methods, but never before OnStateEntered nor after OnStateExit.</summary>
        public virtual void LateUpdate() { }

        /// <summary>Runs at <c>DoAIInterval</c>, which the interval depends on EnemyAI's <c>AIIntervalTime</c>.</summary>
        public virtual void AIInterval() { }

        public virtual void OnCollideWithPlayer(Collider other) { }

        public abstract IEnumerator OnStateExit();

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
        public virtual void OnCollideWithPlayer(Collider other) { }
    }

    public enum PlayerState
    {
        Dead,
        Outside,
        Inside,
        Ship
    }

    internal AIBehaviorState ActiveState = null!;
    internal System.Random enemyRandom = null!;
    internal float AITimer;
    internal bool PrintDebugs = false;
    internal PlayerState MyValidState = PlayerState.Inside;
    internal AIStateTransition nextTransition = null!;
    internal List<AIStateTransition> GlobalTransitions = new List<AIStateTransition>();
    internal List<AIStateTransition> AllTransitions = new List<AIStateTransition>();
    /// <summary>
    /// The instance of this enemy.
    /// </summary>
    internal T self = default!;
    private PlayerControllerB? _lastSyncedTargetPlayer;

    /// <summary>
    /// A property that works as a networked wrapper for the base-game's
    /// <see cref="EnemyAI.targetPlayer"/> field.
    /// </summary>
    public new PlayerControllerB? targetPlayer
    {
        get
        {
            if (IsOwner && base.targetPlayer != _lastSyncedTargetPlayer)
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
            if (value == _lastSyncedTargetPlayer && _lastSyncedTargetPlayer == base.targetPlayer)
                return;
            if (value is not null)
                SetTargetServerRpc((int)value.actualClientId);
            else
                SetTargetServerRpc(-1);
        }
    }
    private Coroutine? _transitionCoroutineInProgress = null;
    private Dictionary<string, Type> _typeNameToType = [];

    /// <summary>
    /// A method to get the instance of the enemy class.
    /// </summary>
    /// <returns><see langword="this"/></returns>
    internal abstract T GetThis();

    /// <summary>
    /// Used for setting the initial <see cref="AIBehaviorState"/> for the state machine.
    /// </summary>
    /// <returns></returns>
    internal abstract AIBehaviorState GetInitialState();

    public override string __getTypeName()
    {
        return GetType().Name;
    }

    public override void Start()
    {
        //Initializers
        self = GetThis();
        ActiveState = GetInitialState();
        enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);

        MyValidState = enemyType.isOutsideEnemy ? PlayerState.Outside : PlayerState.Inside;

        //Fix for the animator sometimes deciding to just not work
        creatureAnimator.Rebind();

        base.Start();

        //Debug to make sure that the agent is actually on the NavMesh
        if (!agent.isOnNavMesh && base.IsOwner)
        {
            DebugLog("CREATURE " + __getTypeName() + " WAS NOT PLACED ON NAVMESH, DESTROYING...");
            KillEnemyOnOwnerClient();
        }

        _transitionCoroutineInProgress = StartCoroutine(InitializeState(ActiveState, self, enemyRandom));
    }

    public override void Update()
    {
        if (isEnemyDead)
        {
            return;
        }
        base.Update();
        AITimer += Time.deltaTime;

        if (_transitionCoroutineInProgress is not null) return;
        bool currentlyNotTransitioningState = true;

        //Reset transition list to match all those in our current state, along with any global transitions that exist regardless of state (stunned, mostly)
        AllTransitions.Clear();
        AllTransitions.AddRange(GlobalTransitions);
        AllTransitions.AddRange(ActiveState.Transitions);

        foreach (AIStateTransition TransitionToCheck in AllTransitions)
        {
            InitializeStateTransition(TransitionToCheck, self);
            if (TransitionToCheck.CanTransitionBeTaken() && base.IsOwner)
            {
                currentlyNotTransitioningState = false;
                nextTransition = TransitionToCheck;
                TransitionStateServerRpc(nextTransition.ToString());
                return;
            }
        }

        if (currentlyNotTransitioningState)
        {
            ActiveState.Update();
        }
    }

    internal void LateUpdate()
    {
        ActiveState.LateUpdate();
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();
        if (_transitionCoroutineInProgress is not null) return;
        ActiveState.AIInterval();
    }

    public override void OnCollideWithPlayer(Collider other)
    {
        base.OnCollideWithPlayer(other);
        ActiveState.OnCollideWithPlayer(other);
        foreach (AIStateTransition TransitionToCheck in AllTransitions)
        {
            InitializeStateTransition(TransitionToCheck, self);
            TransitionToCheck.OnCollideWithPlayer(other);
        }
    }

    private IEnumerator InitializeState(AIBehaviorState ActiveState, T self, System.Random enemyRandom)
    {
        ActiveState.self = self;
        ActiveState.agent = self.agent;
        ActiveState.enemyRandom = enemyRandom;
        ActiveState.creatureAnimator = self.creatureAnimator;
        yield return StartCoroutine(ActiveState.OnStateEntered());
        _transitionCoroutineInProgress = null;
    }

    private void InitializeStateTransition(AIStateTransition transition, T self)
    {
        // We can assume everything is initialized if this is
        if (transition.self is not null)
            return;

        transition.self = self;
        transition.agent = self.agent;
        transition.enemyRandom = self.enemyRandom;
        transition.creatureAnimator = self.creatureAnimator;
    }

    internal void DebugLog(object data)
    {
        if (PrintDebugs)
            PLog.Log(data);
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
        _transitionCoroutineInProgress = StartCoroutine(TransitionState(stateName, randomSeed));

    internal IEnumerator TransitionState(string stateOrTransitionName, int randomSeed)
    {
        AIStateTransition? localNextTransition = null;
        if (!_typeNameToType.TryGetValue(stateOrTransitionName, out Type? transitionOrBehaviorType))
        {
            transitionOrBehaviorType = Type.GetType(stateOrTransitionName);

            if (transitionOrBehaviorType is null)
                throw new ArgumentException($"'{stateOrTransitionName}' wasn't found as a type!",
                    nameof(stateOrTransitionName));

            _typeNameToType.Add(stateOrTransitionName, transitionOrBehaviorType);
        }

        if (transitionOrBehaviorType.IsSubclassOf(typeof(AIStateTransition)))
        {
            localNextTransition = (AIStateTransition)Activator.CreateInstance(transitionOrBehaviorType);
            InitializeStateTransition(localNextTransition, self);
            if (localNextTransition.NextState().GetType() == ActiveState.GetType())
                yield break;
        }
        else if (!transitionOrBehaviorType.IsSubclassOf(typeof(AIBehaviorState)))
            throw new ArgumentException(
                $"'{stateOrTransitionName}' is neither an {nameof(AIStateTransition)} nor an {nameof(AIBehaviorState)}!",
                nameof(stateOrTransitionName));

        //LogMessage(stateName);
        DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Exiting:  {ActiveState}");
        yield return StartCoroutine(ActiveState.OnStateExit());

        if (localNextTransition is not null)
        {
            DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Transitioning via:  {localNextTransition}");
            ActiveState = localNextTransition.NextState();
        }
        else
        {
            DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Transitioning via: State Override");
            ActiveState = (AIBehaviorState)Activator.CreateInstance(transitionOrBehaviorType);
        }

        DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Entering:  {ActiveState}");
        StartCoroutine(InitializeState(ActiveState, self, new(randomSeed)));

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
            base.targetPlayer = null;
            _lastSyncedTargetPlayer = null;
            DebugLog($"Clearing target on {this}");
            return;
        }
        if (StartOfRound.Instance.allPlayerScripts[PlayerID] == null)
        {
            DebugLog($"Index invalid! {this}");
            return;
        }
        base.targetPlayer = StartOfRound.Instance.allPlayerScripts[PlayerID];
        _lastSyncedTargetPlayer = base.targetPlayer;
        DebugLog($"{this} setting target to: {base.targetPlayer.playerUsername}");
    }
}
