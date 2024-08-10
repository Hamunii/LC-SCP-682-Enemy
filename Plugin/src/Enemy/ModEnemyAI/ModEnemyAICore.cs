/*
 * This class is based on WTOEnemy from Welcome To Ooblterra:
 * https://github.com/Skeleton-Studios/Welcome_To_Ooblterra/blob/master/Enemies/WTOEnemy.cs
 *
 * Welcome To Ooblterra is licensed under the MIT license, which can be found here:
 * https://github.com/Skeleton-Studios/Welcome_To_Ooblterra/blob/master/LICENSE
 *
 * This class has been modified, and is licensed under the MIT license.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.SCPEnemy;

// Heavily based on WelcomeToOoblterra's WTOEnemy class
public abstract partial class ModEnemyAI<T> : EnemyAI
    where T : ModEnemyAI<T>
{
    /// <summary>
    /// The base class for behavior states. Only one behavior state can be active at once,<br/>
    /// and new behavior states can be transitioned to from <see cref="AIStateTransition"/>s in the<br/>
    /// <see cref="Transitions"/> list.
    /// </summary>
    public abstract class AIBehaviorState : AIBase
    {
        /// <summary>
        /// Called when this state is entered.
        /// </summary>
        /// <remarks>
        /// While this method is still running, <see cref="AIStateTransition"/>s,
        /// and other methods in this <see cref="AIBehaviorState"/> aren't processed.
        /// </remarks>
        public abstract IEnumerator OnStateEntered();

        /// <summary>
        /// Runs every frame, but never before OnStateEntered not after OnStateExit.
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// Runs every frame after the normal Update methods, but never before OnStateEntered nor after OnStateExit.
        /// </summary>
        public virtual void LateUpdate() { }

        /// <summary>Runs at <see cref="EnemyAI.DoAIInterval"/>, which the interval is <see cref="EnemyAI.AIIntervalTime"/>.</summary>
        public virtual void AIInterval() { }

        /// <summary>
        /// Called when a GameObject with a player tag collides with a
        /// GameObject with an EnemyAICollisionDetect script.<br/>
        /// You can use <see cref="ModEnemyAI.TryGetValidPlayerFromCollision(Collider, out PlayerControllerB?)"/>
        /// to get a player from the collider.
        /// </summary>
        /// <remarks>
        /// This will get called even if a state transition is in progress.
        /// </remarks>
        public virtual void OnCollideWithPlayer(Collider other) { }

        /// <summary>
        /// Called after a <see cref="AIStateTransition.CanTransitionBeTaken"/> method has returned <see langword="true"/>.
        /// </summary>
        public abstract IEnumerator OnStateExit();

        /// <summary>
        /// All the transitions that can be made from current State, excluding global transitions.
        /// </summary>
        public abstract List<AIStateTransition> Transitions { get; set; }
    }

    /// <summary>
    /// The base class for state transitions. A single behavior state can have multiple transitions,<br/>
    /// and the first that satisfies the transition conditions will decide the next <see cref="AIBehaviorState"/>.
    /// </summary>
    public abstract class AIStateTransition : AIBase
    {
        /// <summary>
        /// A method called by the state machine manager to check if this transition can be executed.
        /// </summary>
        /// <remarks>
        /// Called every frame, except during state enter or exit.<br/>
        /// If true is returned, <see cref="NextState"/> is called and returns the next state.
        /// </remarks>
        /// <returns><see langword="true"/> if transition should be executed, <see langword="false"/> otherwise.</returns>
        public abstract bool CanTransitionBeTaken();

        /// <summary>
        /// Returns the next state for the state machine.
        /// </summary>
        /// <returns>The next <see cref="AIBehaviorState"/>.</returns>
        public abstract AIBehaviorState NextState();

        /// <inheritdoc cref="AIBehaviorState.OnCollideWithPlayer(Collider)"/>
        public virtual void OnCollideWithPlayer(Collider other) { }
    }

    /// <summary>
    /// A base class for <see cref="AIBehaviorState"/> and <see cref="AIStateTransition"/>
    /// that provides an instance of the enemy,<br/>
    /// and some "shortcuts" to commonly used fields via properties.
    /// </summary>
    public abstract class AIBase
    {
        /// <inheritdoc cref="ModEnemyAI{T}.self"/>
        public T self = default!;

        /// <summary>
        /// The NavMeshAgent of this enemy.
        /// </summary>
        public NavMeshAgent Agent => self.agent;

        /// <inheritdoc cref="ModEnemyAI{T}.targetPlayer"/>
        public PlayerControllerB? TargetPlayer { get => self.targetPlayer; set => self.targetPlayer = value; }

        /// <inheritdoc cref="ModEnemyAI{T}.enemyRandom"/>
        public System.Random EnemyRandom => self.enemyRandom;
        public Animator CreatureAnimator => self.creatureAnimator;
        public AudioSource CreatureSFX => self.creatureSFX;
        public AudioSource CreatureVoice => self.creatureVoice;
    }

    private class TransitionType(Type type, bool isTransition)
    {
        internal readonly Type type = type;
        internal readonly bool isTransition = isTransition;
    }

    private static readonly Dictionary<(string, Type), TransitionType> _typeNameAndInstanceTypeToTransitionType = [];

    public enum PlayerState
    {
        Dead,
        Outside,
        Inside,
        Ship
    }

    internal AIBehaviorState activeState = null!;

    /// <summary>
    /// A seeded random number generator, that's synced each time state is changed.
    /// </summary>
    internal System.Random enemyRandom = null!;
    internal float AITimer;
    internal bool printDebugs = false;
    internal PlayerState myValidState = PlayerState.Inside;
    internal AIStateTransition nextTransition = null!;
    internal List<AIStateTransition> globalTransitions = [];
    /// <summary>
    /// The instance of this enemy.
    /// </summary>
    internal T self = default!;
    private PlayerControllerB? _lastSyncedTargetPlayer;

    /// <summary>
    /// A property that works as a networked wrapper for the base-game's
    /// <see cref="EnemyAI.targetPlayer"/> field.
    /// </summary>
#pragma warning disable IDE1006 // Naming Styles
    // We want this to work as a seamless replacement for the 'targetPlayer' field.
    public new PlayerControllerB? targetPlayer
#pragma warning restore IDE1006 // Naming Styles
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
        activeState = GetInitialState();
        enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);

        myValidState = enemyType.isOutsideEnemy ? PlayerState.Outside : PlayerState.Inside;

        //Fix for the animator sometimes deciding to just not work
        creatureAnimator.Rebind();

        base.Start();

        //Debug to make sure that the agent is actually on the NavMesh
        if (!agent.isOnNavMesh && base.IsOwner)
        {
            DebugLog("CREATURE " + __getTypeName() + " WAS NOT PLACED ON NAVMESH, DESTROYING...");
            KillEnemyOnOwnerClient();
        }

        _transitionCoroutineInProgress = StartCoroutine(InitializeAndEnterState(activeState, self, enemyRandom));
    }

    public override void Update()
    {
        if (isEnemyDead)
        {
            return;
        }
        base.Update();
        AITimer += Time.deltaTime;

        if (_transitionCoroutineInProgress is not null)
            return;

        foreach (AIStateTransition TransitionToCheck in GetAllTransitions())
        {
            if (TransitionToCheck.CanTransitionBeTaken() && base.IsOwner)
            {
                nextTransition = TransitionToCheck;
                TransitionStateServerRpc(nextTransition.ToString());
                return;
            }
        }

        activeState.Update();
    }

    internal void LateUpdate()
    {
        if (_transitionCoroutineInProgress is not null)
            return;

        activeState.LateUpdate();
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();

        if (_transitionCoroutineInProgress is not null)
            return;

        activeState.AIInterval();
    }

    public override void OnCollideWithPlayer(Collider other)
    {
        base.OnCollideWithPlayer(other);

        activeState.OnCollideWithPlayer(other);
        foreach (AIStateTransition transitionToCheck in GetAllTransitions())
        {
            transitionToCheck.OnCollideWithPlayer(other);
        }
    }

    private IEnumerable<AIStateTransition> GetAllTransitions()
    {
        foreach (AIStateTransition transition in globalTransitions)
        {
            InitializeStateTransition(transition, self);
            yield return transition;
        }

        foreach (AIStateTransition transition in activeState.Transitions)
        {
            InitializeStateTransition(transition, self);
            yield return transition;
        }
    }

    private IEnumerator InitializeAndEnterState(AIBehaviorState activeState, T self, System.Random enemyRandom)
    {
        activeState.self = self;
        yield return StartCoroutine(activeState.OnStateEntered());
        _transitionCoroutineInProgress = null;
    }

    private void InitializeStateTransition(AIStateTransition transition, T self) =>
        transition.self = self;

    internal void DebugLog(object data)
    {
        if (printDebugs)
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

        if (!_typeNameAndInstanceTypeToTransitionType.TryGetValue((stateOrTransitionName, self.GetType()), out TransitionType? transitionOrBehavior))
            ValidateAndCacheTransitionType(stateOrTransitionName, ref transitionOrBehavior);

        if (transitionOrBehavior.isTransition)
        {
            localNextTransition = (AIStateTransition)Activator.CreateInstance(transitionOrBehavior.type);
            InitializeStateTransition(localNextTransition, self);
            if (localNextTransition.NextState().GetType() == activeState.GetType())
                yield break;
        }

        //LogMessage(stateName);
        DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Exiting:  {activeState}");
        yield return StartCoroutine(activeState.OnStateExit());

        if (localNextTransition is not null)
        {
            DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Transitioning via:  {localNextTransition}");
            activeState = localNextTransition.NextState();
        }
        else
        {
            DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Transitioning via: State Override");
            activeState = (AIBehaviorState)Activator.CreateInstance(transitionOrBehavior.type);
        }

        DebugLog($"{__getTypeName()} #{self.thisEnemyIndex} is Entering:  {activeState}");
        StartCoroutine(InitializeAndEnterState(activeState, self, new(randomSeed)));

        //Debug Prints
        StartOfRound.Instance.ClientPlayerList.TryGetValue(
            NetworkManager.Singleton.LocalClientId,
            out var value
        );
        DebugLog(
            $"CREATURE: {enemyType.name} #{self.thisEnemyIndex} STATE: {activeState} ON PLAYER: #{value} ({StartOfRound.Instance.allPlayerScripts[value].playerUsername})"
        );
    }

    /// <exception cref="ArgumentException"/>
    private void ValidateAndCacheTransitionType(string stateOrTransitionName, [NotNull] ref TransitionType? transitionOrBehavior)
    {
        Type newType = Type.GetType(stateOrTransitionName)
            ?? throw new ArgumentException($"'{stateOrTransitionName}' wasn't found as a type!",
                nameof(stateOrTransitionName));

        if (newType.IsSubclassOf(typeof(AIStateTransition)))
        {
            transitionOrBehavior = new TransitionType(newType, isTransition: true);
            _typeNameAndInstanceTypeToTransitionType.Add((stateOrTransitionName, self.GetType()), transitionOrBehavior);
            return;
        }
        else if (newType.IsSubclassOf(typeof(AIBehaviorState)))
        {
            transitionOrBehavior = new TransitionType(newType, isTransition: false);
            _typeNameAndInstanceTypeToTransitionType.Add((stateOrTransitionName, self.GetType()), transitionOrBehavior);
            return;
        }

        throw new ArgumentException(
            $"'{stateOrTransitionName}' is neither an {nameof(AIStateTransition)} nor an {nameof(AIBehaviorState)}!",
            nameof(stateOrTransitionName));
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
