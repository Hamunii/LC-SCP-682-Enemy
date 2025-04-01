using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using GameNetcodeStuff;
using SCP682.Hooks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.SCPEnemy;

class SCP682AI : ModEnemyAI<SCP682AI>, IVisibleThreat
{
    #region Initialization
    // We use this list to destroy loaded game objects when plugin is reloaded
    internal static List<GameObject> SCP682Objects = [];

    public enum Speed
    {
        Stopped = 0,
        Walking = 3,
        Running = 10
    }

    public enum EnemyScale
    {
        Small = 0,
        Big = 1,
    }

    static class Anim
    {
        // do: trigger
        // is: boolean
        internal const string isMoving = "isMoving";
        internal const string isMovingInverted = "isMovingInverted";
        internal const string isRunning = "isRunning";
        internal const string isOnShip = "isOnShip";
        internal const string doKillEnemy = "KillEnemy"; // base game thing, gets called automatically
        internal const string doRevive = "doRevive";
        internal const string doBite = "doBite";
        internal const string doRoar = "doRoar";
    }

    const float defaultBoredOfWanderingFacilityTimer = 120f;
    float boredOfWanderingFacilityTimer = defaultBoredOfWanderingFacilityTimer;
    Vector3 PosOnTopOfShip { get => StartOfRound.Instance.insideShipPositions[0].position + new Vector3(-2, 5, 3); }
    MonoBehaviour? targetEnemy = null!;
    internal List<MonoBehaviour?> blacklistedEnemies = [];

    internal PlayerControllerB? PlayerHeardFromNoise
    {
        get
        {
            if (_playerHeardFromNoiseTimer > 1)
                return null;

            return _playerHeardFromNoise;
        }
        set => _playerHeardFromNoise = value;
    }
    private PlayerControllerB? _playerHeardFromNoise;
    private float _playerHeardFromNoiseTimer = 0f;
    internal bool roarAttackInProgress = false;

    LineRenderer lineRenderer = null!;
    BoxCollider mainCollider = null!;

    internal Transform turnCompass = null!;
    internal Transform crocodileModel = null!;
    internal Transform reversedMouthPosition = null!;
    const float defaultAttackCooldown = 5f;
    float attackCooldown = defaultAttackCooldown;
    Coroutine? changeScaleCoroutine;

    private List<PlayerControllerB> playersAttackedSelf = [];
    

    private int _defaultHealth;

    internal AudioSource globalAudio = null!;

    /// <summary>Used for https://docs.unity3d.com/ScriptReference/Physics.OverlapSphereNonAlloc.html</summary>
    internal static Collider[] tempCollisionArr = new Collider[20];
    internal NavMeshPath? tempPath = new();

    // Unused in game?
    ThreatType IVisibleThreat.type => ThreatType.RadMech;

    // Something about how much other enemies are scared of this enemy.
    // 10 taken as value because RadMechAI gives 10 at max.
    int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition) => 10;

    // Seems to be unused by enemies, so we default to 0.
    int IVisibleThreat.GetInterestLevel() => 0;

    Transform IVisibleThreat.GetThreatLookTransform() => eye;

    Transform IVisibleThreat.GetThreatTransform() => transform;

    // This is basically used by other enemies (like the Baboon Hawk) to predict this enemy's future position.
    Vector3 IVisibleThreat.GetThreatVelocity() => IsOwner ? agent.velocity : Vector3.zero;

    // Basically, this is how well other enemies can sense this enemy.
    float IVisibleThreat.GetVisibility()
    {
        if (enemyHP <= 0)
            return 0f;

        return 1f;
    }

    // Unused in game.
    int IVisibleThreat.SendSpecialBehaviour(int id) => 0;

    // Defined in BaboonBirdAI and RadMechAI, has Player and Enemies layers
    internal const int visibleThreatsMask = 524296;

    internal override SCP682AI GetThis() => this;
    internal override AIBehaviorState GetInitialState() => new WanderThroughEntranceState();

    public override void Start()
    {
        SCP682Objects.Add(gameObject);

        _defaultHealth = enemyHP;

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        mainCollider = gameObject.GetComponentInChildren<BoxCollider>();

        turnCompass = transform.Find("TurnCompass").GetComponent<Transform>();
        crocodileModel = transform.Find("ModelRoot");
        reversedMouthPosition = crocodileModel.Find("CrocodileModel/ReversedMouthPosition");

        globalAudio = GetComponent<AudioSource>();

        var scale = 1f;
        crocodileModel.localScale = new(scale, scale, scale);

#if DEBUG || true
        printDebugs = true;
#endif

        if (ModMenuAPICompatibility.Enabled)
            ModMenuAPICompatibility.InitDebug(this);
        else
        {
#if DEBUG
            PLog.LogWarning("Hamunii.ModMenuAPI not installed, debug UI can't be shown!");
#endif
        }

        /// The enemy can spawn either inside or outside in <see cref="ApparatusTakenHook.SpawnSCP682(bool)"/>.
        /// The `isOutsideEnemy` variable isn't networked which will lead to initial size desync half of the time,
        /// so we RPC this here on Start.
        if (IsHost)
        {
            if (enemyType.isOutsideEnemy)
                SyncScaleOnSpawnClientRPC(EnemyScale.Big);
            else
                SyncScaleOnSpawnClientRPC(EnemyScale.Small);
        }

        // creatureSFX.clip = SFX.walk.FromRandom(enemyRandom);
        // creatureSFX.loop = true;
        // creatureSFX.Play();
        // agent.radius = 0.5f;
        base.Start();

        globalAudio.PlayOneShot(SFX.spawn.FromRandom(enemyRandom));

        DebugLog($"Am I an outside enemy? {enemyType.isOutsideEnemy}");
    }

    [ClientRpc]
    private void SyncScaleOnSpawnClientRPC(EnemyScale scale)
    {
        if (scale is EnemyScale.Big)
            SetEnemyOutside(true);
        else
            SetEnemyOutside(false);

        changeScaleCoroutine = StartCoroutine(ChangeEnemyScaleTo(EnemyScale.Big));
    }

    public override void Update()
    {
        attackCooldown -= Time.deltaTime;
        _playerHeardFromNoiseTimer += Time.deltaTime;
        base.Update();
    }

    public override void KillEnemy(bool destroy = false)
    {
        // We want all enemies to attack 682 so `enemyType.canDie` is `true`,
        // therefore we need to prevent 682 from forcefully dying like this.
    }

#if DEBUG
    public override void DoAIInterval()
    {
        base.DoAIInterval();
        StartCoroutine(DrawPath(lineRenderer, agent));
    }
#endif

    /// <param name="bashDoor">Delegate to do the actual door bashing.</param>
    public IEnumerator BashDoorAnimation(Action bashDoor)
    {
        // What we want is:
        // - If 682 is walking, perform bite animation
        // - If 682 is running, run through the door

        if (activeState is InvestigatePlayerState)
        {
            AnimatorSetTrigger(Anim.doBite);
            yield return new WaitForSeconds(0.2f);
        }

        bashDoor();
    }

    public override void HitEnemy(
        int force = 1,
        PlayerControllerB? playerWhoHit = null,
        bool playHitSFX = false,
        int hitID = -1
    )
    {
        // We don't want the enemy to wake up instantly if it's already 'dead'.
        if (enemyHP <= 0 && activeState is DeadTemporarilyState)
            return;

        if (playerWhoHit != null)
        {
            if (playersAttackedSelf.Count == 0)
            {
                playersAttackedSelf.Add(playerWhoHit);
                creatureVoice.PlayOneShot(SFX.Voice.Pathetic_HitByPlayerFirstTime);
            }
            else if (!playersAttackedSelf.Contains(playerWhoHit))
            {
                playersAttackedSelf.Add(playerWhoHit);
                creatureVoice.PlayOneShot(SFX.Voice.LoathsomeParasites_MultiplePlayersAttacking);
            }
        }


        base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);

        if (enemyHP > 0)
            creatureSFX.PlayOneShot(SFX.hit.FromRandom(enemyRandom));

        enemyHP -= force;
        if (enemyHP <= 0 && activeState is not DeadTemporarilyState)
        {
            // This enemy never dies for real.
            // Therefore we never call KillEnemyOnOwnerClient();

            OverrideState(new DeadTemporarilyState());
            return;
        }

        if (playerWhoHit != null && activeState is not AttackPlayerState)
            OverrideState(new AttackPlayerState());
    }

    public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
    {
        base.DetectNoise(noisePosition, noiseLoudness, timesPlayedInOneSpot, noiseID);

        if (noiseLoudness < 0.4f)
            return;

        foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
        {
            if (player.isPlayerDead || !player.isPlayerControlled)
                continue;

            if (Vector3.Distance(noisePosition, player.transform.position) < 3)
            {
                PlayerHeardFromNoise = player;
                _playerHeardFromNoiseTimer = 0f;
                return;
            }
        }
    }

    #endregion
    #region Outside States

    private class WanderToShipState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [
                new OnShipAmbushState.ArrivedAtShipTransition(),
                new InvestigatePlayerTransition(),
                new AttackEnemyState.TargetEnemyTransition()
            ];

        public override IEnumerator OnStateEntered()
        {
            self.AnimatorSetBool(Anim.isMoving, true);
            yield break;
        }

        public override void AIInterval()
        {
            self.SetDestinationToPosition(self.PosOnTopOfShip);
        }

        public override IEnumerator OnStateExit() { yield break; }
    }

    private class OnShipAmbushState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AmbushPlayerFromShipTransition(), new BoredOfAmbushTransition()];

        public override IEnumerator OnStateEntered()
        {
            self.AnimatorSetBool(Anim.isMoving, false);
            self.AnimatorSetBool(Anim.isOnShip, true);

            // Make sure we aren't still pathfinding anywhere else
            // this probably doesn't even work, but it's for testing anyways
            Agent.SetDestination(Agent.transform.position);
            self.destination = Agent.transform.position;

            Agent.enabled = false;

            self.gameObject.transform.position = self.PosOnTopOfShip;

            if (self.changeScaleCoroutine != null)
                self.StopCoroutine(self.changeScaleCoroutine);
            self.StartCoroutine(self.ChangeEnemyScaleTo(EnemyScale.Small));
            yield break;
        }

        public override void Update()
        {
            if (TargetPlayer == null)
                return;

            self.turnCompass.LookAt(TargetPlayer.transform);
            self.transform.rotation = Quaternion.Lerp(
                self.transform.rotation,
                Quaternion.Euler(new Vector3(0f, self.turnCompass.eulerAngles.y, 0f)),
                1f * Time.deltaTime);
        }

        public override IEnumerator OnStateExit()
        {
            self.AnimatorSetBool(Anim.isMoving, true);
            self.AnimatorSetBool(Anim.isOnShip, false);

            Agent.enabled = true;

            if (self.changeScaleCoroutine != null)
                self.StopCoroutine(self.changeScaleCoroutine);
            self.StartCoroutine(self.ChangeEnemyScaleTo(EnemyScale.Big));
            yield break;
        }

        internal class ArrivedAtShipTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                if (Vector3.Distance(self.PosOnTopOfShip, self.gameObject.transform.position) < 3f)
                    return true;
                return false;
            }

            public override AIBehaviorState NextState() => new OnShipAmbushState();
        }

        private class AmbushPlayerFromShipTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                if (self.PlayerHeardFromNoise != null)
                    TargetPlayer = self.PlayerHeardFromNoise;
                else
                {
                    // Assign target player anyways so the enemy visually looks towards the closest player
                    TargetPlayer = self.FindNearestPlayer();
                    return false;
                }

                if (!self.PlayerWithinRange(TargetPlayer, 30f))
                    return false;

                return true;
            }

            public override AIBehaviorState NextState() => new FromAmbushJumpPlayerState();
        }

        private class BoredOfAmbushTransition : AIStateTransition
        {
            float boredOfAmbushTimer = 35f;

            public override bool CanTransitionBeTaken()
            {
                boredOfAmbushTimer -= Time.deltaTime;
                if (boredOfAmbushTimer <= 0)
                    return true;
                return false;
            }

            public override AIBehaviorState NextState() => new WanderThroughEntranceState();
        }
    }

    private class FromAmbushJumpPlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new TouchTargetPlayerAndStartDraggingTransition(), new LostPlayerTransition()];

        public override IEnumerator OnStateEntered()
        {
            if (TargetPlayer == null)
            {
                Plugin.Logger.LogError("Trying to ambush player, but targetPlayer is null! Not jumping.");
                self.StartCoroutine(self.RoarAndRunCoroutine());
                yield break;
            }

            if (TargetPlayer.isInHangarShipRoom)
            {
                self.DebugLog("TargetPlayer is inside ship, attacking without a jump.");
                self.StartCoroutine(self.RoarAndRunCoroutine());
                yield break;
            }

            Vector3 positionBehindPlayer;

            {
                Vector3 targetPositionBehindPlayer = TargetPlayer.transform.position - Vector3.Scale(new Vector3(10, 0, 10), TargetPlayer.transform.forward);

                if (!NavMesh.SamplePosition(targetPositionBehindPlayer, out NavMeshHit navHit, maxDistance: 10f, NavMesh.AllAreas))
                {
                    Plugin.Logger.LogWarning("Trying to ambush player, but didn't find NavMesh near target player! Not jumping.");
                    self.StartCoroutine(self.RoarAndRunCoroutine());
                    yield break;
                }

                positionBehindPlayer = navHit.position;
            }

            Vector3 positionInBetweenInAir = (positionBehindPlayer + positionBehindPlayer) / 2 + (Vector3.up * 10f);
            Vector3 originalPosition = self.transform.position;

            // self.SetAgentSpeedAndAnimations(Speed.Stopped);
            Agent.speed = (int)Speed.Stopped;
            self.AnimatorSetBool(Anim.isRunning, true); // isMoving is current false, so we go directly to running.
            Agent.enabled = false;

            // Everything is now validated and set up, we can perform the jump animation.
            float normalizedTimer = 0f;
            while (normalizedTimer <= 1f)
            {
                float scaledDeltaTime = Time.deltaTime / 1.5f; // Jump lasts for <divider> seconds.
                normalizedTimer += scaledDeltaTime;

                // This is a Bezier curve.
                Vector3 m1 = Vector3.Lerp(originalPosition, positionInBetweenInAir, normalizedTimer);
                Vector3 m2 = Vector3.Lerp(positionInBetweenInAir, positionBehindPlayer, normalizedTimer);
                self.transform.position = Vector3.Lerp(m1, m2, normalizedTimer);

                // self.transform.Rotate(0, 180 / scaledDeltaTime, 0, Space.World);
                yield return null;
            }

            self.transform.position = positionBehindPlayer;
            Agent.enabled = true;
            Agent.Warp(positionBehindPlayer);

            self.AnimatorSetBool(Anim.isRunning, false); // Now we set running to false so we can go from walking to running again.
            self.StartCoroutine(self.RoarAndRunCoroutine());
            yield break;
        }

        public override void AIInterval()
        {
            TargetPlayer = self.FindNearestPlayer();
            self.SetDestinationToPosition(TargetPlayer.transform.position);
        }

        public override IEnumerator OnStateExit()
        {
            self.SetAgentSpeedAndAnimations(Speed.Walking);
            yield break;
        }

        private class TouchTargetPlayerAndStartDraggingTransition : AIStateTransition
        {
            // I dunno how bad this is for performance
            public override bool CanTransitionBeTaken() => self.IsPlayerInsideCollider(TargetPlayer, self.mainCollider);
            public override AIBehaviorState NextState() => new DragPlayerState();
        }
    }

    private class DragPlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new DraggedPlayerEnoughTransition()];

        EntranceTeleport facilityEntrance = null!;
        float realMouthOrStableReverseMouth = 0;

        bool exitingThisState = false;

        public override IEnumerator OnStateEntered()
        {
            TargetPlayer = self.FindNearestPlayer();
            self.EnterSpecialAnimationWithPlayer(TargetPlayer, stopMovementCalculations: false);

            if (self.IsOwner)
            {
                facilityEntrance = RoundManager.FindMainEntranceScript(self.isOutside);
                if (facilityEntrance == null)
                {
                    PLog.LogError("Can't pathfind to entrance because it doesn't exist.");
                    yield break;
                }

                if (!self.SetDestinationToPosition(facilityEntrance.entrancePoint.position, true))
                {
                    PLog.LogError("Facility door is unreachable!");
                    yield break;
                }
            }

            self.AnimatorSetBool(Anim.isMovingInverted, true);
            self.PlayVoice(SFX.VoiceCode.TearYouApart_DraggingPlayer);

            Agent.speed = (int)Speed.Stopped;

            // This is kind of a mess, but there is no proper place for the mouth so we pick a position
            // that is parented to 682's pelvis, and then change that to a static (relative) position behind 682.

            realMouthOrStableReverseMouth = 0f;

            float normalizedTimer = 0f;
            while (normalizedTimer <= 4.2f)
            {
                normalizedTimer += Time.deltaTime;
                Update();
                yield return null;
            }

            Agent.speed = (int)Speed.Walking;

            if (self.IsOwner)
                AIInterval();

            normalizedTimer = 0f;
            while (normalizedTimer < 1f)
            {
                normalizedTimer += Time.deltaTime;
                realMouthOrStableReverseMouth = normalizedTimer;
                Update();
                yield return null;
            }

            realMouthOrStableReverseMouth = 1f;
            Agent.speed = 7;
        }

        public override void AIInterval()
        {
            if (self.IsOwner && !self.SetDestinationToPosition(facilityEntrance.entrancePoint.position, true))
                PLog.LogError("Facility door is unreachable!");
        }

        // Works better than LateUpdate, that one fucks up the helmet overlay thingy position
        public override void Update()
        {
            if (self.inSpecialAnimationWithPlayer == null)
            {
                // If we are exiting the state, we must not set special animation with player
                // or the player will get stuck.
                if (exitingThisState)
                    return;

                Plugin.Logger.LogWarning("Somehow `inSpecialAnimationWithPlayer` is null while dragging, setting it anyways.");
                self.CancelSpecialAnimationWithPlayer();
                if (TargetPlayer == null)
                {
                    Plugin.Logger.LogError("Nvm, target player is somehow also null.");
                    return;
                }
                self.EnterSpecialAnimationWithPlayer(TargetPlayer, stopMovementCalculations: false);
                if (self.inSpecialAnimationWithPlayer == null)
                {
                    Plugin.Logger.LogError("Uhh, `inSpecialAnimationWithPlayer` is still null even after setting it? (This should never happen.)");
                    return;
                }
            }
            if (self.inSpecialAnimationWithPlayer.inAnimationWithEnemy != self)
            {
                Plugin.Logger.LogWarning("Player is no longer in special animation with this enemy!");
                self.OverrideState(new AttackPlayerState());
                return;
            }

            self.inSpecialAnimationWithPlayer.transform.position =
            Vector3.Lerp(CreatureVoice.transform.position + new Vector3(0, -1f, 0),
                self.reversedMouthPosition.position,
                realMouthOrStableReverseMouth);
        }

        public override IEnumerator OnStateExit()
        {
            exitingThisState = true;
            Agent.speed = (int)Speed.Stopped;
            self.AnimatorSetBool(Anim.isMovingInverted, false);
            self.CancelSpecialAnimationWithPlayer();

            yield return new WaitForSeconds(2f); // 3.2 seconds is about the duration of the direction change transition.
        }

        private class DraggedPlayerEnoughTransition : AIStateTransition
        {
            float draggedPlayerTimer = 0;
            public override bool CanTransitionBeTaken()
            {
                draggedPlayerTimer += Time.deltaTime;
                if (draggedPlayerTimer > 15)
                    return true;
                return false;
            }

            public override AIBehaviorState NextState() => new AttackPlayerState();
        }
    }

    private class WanderThroughEntranceState : AIBehaviorState
    {
        // Note: We add one more transition to this afterwards!
        public override List<AIStateTransition> Transitions { get; set; } =
            [new InvestigatePlayerTransition(), new AttackEnemyState.TargetEnemyTransition()];
        EntranceTeleport facilityEntrance = null!;

        public override IEnumerator OnStateEntered()
        {
            self.AnimatorSetBool(Anim.isMoving, true);

            facilityEntrance = RoundManager.FindMainEntranceScript(self.isOutside);
            Transitions.Add(new EnterEntranceTransition());
            yield break;
        }

        public override void AIInterval()
        {
            // TODO: More interesting pathing
            if (!self.SetDestinationToPosition(facilityEntrance.entrancePoint.position, true)) // when checkForPath is true, pathfinding is a little better (can path to the half-obstructed door in test level)
            {
                PLog.LogWarning("Facility door is unreachable! Wandering instead.");
                self.OverrideState(new AtFacilityWanderingState());
            }

        }

        public override IEnumerator OnStateExit()
        {
            if (self.changeScaleCoroutine != null)
                self.StopCoroutine(self.changeScaleCoroutine);
            // isOutside has already been updated, so we change scale according to that.
            if (self.isOutside)
                self.StartCoroutine(self.ChangeEnemyScaleTo(EnemyScale.Big));
            else
                self.StartCoroutine(self.ChangeEnemyScaleTo(EnemyScale.Small));

            yield break;
        }

        public class EnterEntranceTransition : AIStateTransition
        {
            EntranceTeleport? _et;

            public override bool CanTransitionBeTaken()
            {
                _et ??= RoundManager.FindMainEntranceScript(self.isOutside);
                if (Vector3.Distance(_et.entrancePoint.position, self.gameObject.transform.position) < 4.5f)
                {
                    self.TeleportSelfToOtherEntranceClientRpc(self.isOutside);

                    // When we go through the facility door, let's forget blacklisted enemies
                    // so that we can potentially target them again if we can pathfind to them.
                    self.blacklistedEnemies.Clear();
                    return true;
                }
                return false;
            }

            public override AIBehaviorState NextState()
            {
                if (self.isOutside)
                    return new WanderToShipState();
                else
                    return new AtFacilityWanderingState();
            }
        }
    }

    #endregion
    #region Inside States

    private class AtFacilityWanderingState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [
                new BoredOfFacilityTransition(),
                new AtFacilityEatNoisyJesterState.FindNoisyJesterTransition(),
                new InvestigatePlayerTransition(),
                new AttackEnemyState.TargetEnemyTransition()
            ];

        public override IEnumerator OnStateEntered()
        {
            self.AnimatorSetBool(Anim.isMoving, true);

            self.StartSearch(self.transform.position);
            yield break;
        }

        public override IEnumerator OnStateExit()
        {
            self.StopSearch(self.currentSearch);
            yield break;
        }

        private class BoredOfFacilityTransition : AIStateTransition
        {
            float debugMSGTimer = defaultBoredOfWanderingFacilityTimer;

            public override bool CanTransitionBeTaken()
            {
                if (!self.isOutside)
                {
                    // It's possible we are outside as a failsafe for not being able to pathfind to facility door.
                    foreach (JesterAI jester in JesterListHook.GetJesters())
                    {
                        if (jester.currentBehaviourStateIndex != 2) // Hunting state.
                            continue;

                        self.DebugLog(
                            $"[{nameof(BoredOfFacilityTransition)}] Holy hell, a jester is going wild! I'm going to leave the facility."
                        );
                        return true;
                    }
                }

                self.boredOfWanderingFacilityTimer -= Time.deltaTime;
                if (debugMSGTimer - self.boredOfWanderingFacilityTimer > 10)
                {
                    debugMSGTimer = self.boredOfWanderingFacilityTimer;
                    self.DebugLog(
                        $"[{nameof(BoredOfFacilityTransition)}] Time until bored: {self.boredOfWanderingFacilityTimer}"
                    );
                }
                if (self.boredOfWanderingFacilityTimer <= 0f)
                {
                    return true;
                }
                else
                    return false;
            }

            public override AIBehaviorState NextState()
            {
                self.boredOfWanderingFacilityTimer = defaultBoredOfWanderingFacilityTimer;
                return new WanderThroughEntranceState();
            }
        }
    }

    #endregion
    #region Inside (Eat Jester)

    private class AtFacilityEatNoisyJesterState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new NoisyJesterEatenTransition()];

        JesterAI targetJester = null!;
        bool voiceClipPlayed = false;

        public override IEnumerator OnStateEntered()
        {
            if (self.targetEnemy is not JesterAI jester)
            {
                self.OverrideState(new AtFacilityWanderingState());
                yield break;
            }
            targetJester = jester;

            self.AnimatorSetBool(Anim.isMoving, true);
            self.SetAgentSpeedAndAnimations(Speed.Running);
            yield break;
        }

        public override void AIInterval()
        {
            if (self.targetEnemy == null)
                return;

            var jesterPos = targetJester.agent.transform.position;

            if (!voiceClipPlayed && Vector3.Distance(jesterPos, self.transform.position) < 20)
            {
                voiceClipPlayed = true;
                CreatureSFX.PlayOneShot(SFX.Voice.Silence_ChargeJester);
            }

            if (Vector3.Distance(jesterPos, self.transform.position) < 3)
            {
                if (self.IsHost)
                    targetJester.KillEnemyClientRpc(true);
            }

            self.SetDestinationToPosition(jesterPos);
        }

        public override IEnumerator OnStateExit()
        {
            self.SetAgentSpeedAndAnimations(Speed.Walking);
            yield break;
        }

        internal class FindNoisyJesterTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                if (self.isOutside)
                    return false;

                foreach (JesterAI jester in JesterListHook.GetJesters())
                {
                    if (jester.farAudio.isPlaying)
                    {
                        self.targetEnemy = jester;
                        return true;
                    }
                }
                return false;
            }

            public override AIBehaviorState NextState() => new AtFacilityEatNoisyJesterState();
        }

        private class NoisyJesterEatenTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken() => self.targetEnemy == null;

            public override AIBehaviorState NextState() => new AtFacilityWanderingState();
        }
    }

    #endregion
    #region  Special States

    private class DeadTemporarilyState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new DeadTemporarilyUntilAwakeTransition()];

        public override IEnumerator OnStateEntered()
        {
            CreatureVoice.PlayOneShot(SFX.defeated.FromRandom(EnemyRandom));

            Agent.isStopped = true;
            self.AnimatorSetTrigger(Anim.doKillEnemy);
            yield break;
        }

        public override void AIInterval()
        {
            var currentAnimationClip = CreatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip;
            if (currentAnimationClip.name != "CG_death")
            {
                self.DebugLog("Should be dead, but current animation clip isn't dead clip. Setting clip to dead.");
                self.DebugLog("Animation clip was: " + currentAnimationClip.name);
                self.AnimatorSetTrigger(Anim.doKillEnemy);
            }
        }

        public override IEnumerator OnStateExit()
        {
            self.enemyHP = self._defaultHealth;

            Agent.isStopped = false;

            self.AnimatorSetTrigger(Anim.doRevive);

            self.globalAudio.PlayOneShot(SFX.spawn.FromRandom(EnemyRandom));
            self.PlayVoice(SFX.VoiceCode.FullRant_UponRevival);

            self.SetAgentSpeedAndAnimations(Speed.Walking);
            yield break;
        }

        private class DeadTemporarilyUntilAwakeTransition : AIStateTransition
        {
            float timer = 0f;

            public override bool CanTransitionBeTaken()
            {
                timer += Time.deltaTime;

                if (timer > 60f)
                    return true;

                return false;
            }

            public override AIBehaviorState NextState() => new WanderThroughEntranceState();
        }
    }

    internal class AttackEnemyState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new EnemyKilledTransition()];

        int cantPathfindToEnemyCountdown = 10;

        public override IEnumerator OnStateEntered()
        {
            if (self.IsOwner && self.targetEnemy == null)
                yield break;

            yield return self.StartCoroutine(self.RoarAndRunCoroutine());

            void PlayVoice(SFX.VoiceCode clipCode)
                => self.StartCoroutine(self.PlayVoiceInSeconds(clipCode, 3f));

            switch (self.targetEnemy)
            {
                case BaboonBirdAI _: PlayVoice(SFX.VoiceCode.Bothersome_EngageBaboonHawk); break;
                case ForestGiantAI _: PlayVoice(SFX.VoiceCode.Abomination_EngageForestGiant); break;
                case MouthDogAI _: PlayVoice(SFX.VoiceCode.Disgrace_EngageEyelessDog); break;
                default:
                    if (self.targetEnemy is EnemyAI enemyAI)
                    {
                        if (!enemyAI.isOutside)
                            PlayVoice(SFX.VoiceCode.Worms_EngageIndoorEnemies);
                    }
                    break;
            }
        }

        public override void AIInterval()
        {
            if (self.targetEnemy == null)
                return;

            if (!self.SetDestinationToPosition(self.targetEnemy.transform.position, true))
            {
                PLog.LogWarning("Can't pathfind to target enemy!");
                cantPathfindToEnemyCountdown -= 1;

                if (cantPathfindToEnemyCountdown <= 0)
                {
                    PLog.LogWarning("Giving up on target enemy because couldn't reach it.");
                    self.blacklistedEnemies.Add(self.targetEnemy);
                    self.targetEnemy = null;
                }

                if (self.path1.status == NavMeshPathStatus.PathPartial)
                        self.SetDestinationToPosition(self.path1.corners[^1]);
            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI? collidedEnemy)
        {
            if (self.attackCooldown > 0)
                return;

            if (collidedEnemy == null || !self.IsOwner)
                return;

            if (!collidedEnemy.enemyType.canDie || collidedEnemy.isEnemyDead)
                return;

            self.AnimatorSetTriggerServerRpc(Anim.doBite);

            if (collidedEnemy is CaveDwellerAI caveDweller)
            {

                if (self.IsHost)
                {
                    // Meet requirements for it being able to die for real.
                    if (caveDweller.currentBehaviourStateIndex == 0)
                        caveDweller.TransformIntoAdult();
                    else
                        caveDweller.KillEnemyClientRpc(true);
                }
            }
            else
                collidedEnemy.HitEnemyServerRpc(force: 10, -1, true);

            self.attackCooldown = SCP682AI.defaultAttackCooldown;
        }

        public override IEnumerator OnStateExit()
        {
            self.targetEnemy = null;

            self.SetAgentSpeedAndAnimations(Speed.Walking);
            yield break;
        }

        internal class TargetEnemyTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken() => self.targetEnemy != null;

            public override void AIInterval()
            {
                if (self.TryTargetEnemyInProximity(out MonoBehaviour? enemy))
                    self.targetEnemy = enemy;
            }

            public override AIBehaviorState NextState() => new AttackEnemyState();
        }

        private class EnemyKilledTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                if (self.targetEnemy == null)
                    return true;

                if (self.targetEnemy is EnemyAI enemyAI)
                {
                    if (enemyAI.isEnemyDead)
                        return true;
                }

                return false;
            }

            public override AIBehaviorState NextState() => self.CreatePreviousState()!;
        }
    }

    #endregion
    #region Player States

    internal class InvestigatePlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AttackPlayerTransition(), new LostPlayerTransition()];

        public override IEnumerator OnStateEntered()
        {
            self.AnimatorSetBool(Anim.isMoving, true);
            yield break;
        }

        public override void AIInterval()
        {
            TargetPlayer = self.FindNearestPlayer();

            if (!self.SetDestinationToPosition(TargetPlayer.transform.position, true))
            {
                if (self.path1.status == NavMeshPathStatus.PathPartial)
                    self.SetDestinationToPosition(self.path1.corners[^1]);

                self.DoRoarShockwaveAttackIfCan(Speed.Walking);
            }
        }

        public override void OnCollideWithPlayer(Collider other) =>
            self.AttackCollideWithPlayer(other);

        public override IEnumerator OnStateExit()
        {
            if (self.roarAttackInProgress)
                self.StopCoroutine(self.RoarAndRunCoroutine());

            yield break;
        }
    }

    internal class AttackPlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new LostPlayerTransition()];

        public override IEnumerator OnStateEntered()
        {
            self.StartCoroutine(self.RoarAndRunCoroutine());
            yield break;
        }

        public override void AIInterval()
        {
            TargetPlayer = self.FindNearestPlayer();

            if (!self.SetDestinationToPosition(TargetPlayer.transform.position, true))
            {
                if (self.path1.status == NavMeshPathStatus.PathPartial)
                    self.SetDestinationToPosition(self.path1.corners[^1]);

                self.DoRoarShockwaveAttackIfCan(Speed.Running);
            }
        }

        public override void OnCollideWithPlayer(Collider other) =>
            self.AttackCollideWithPlayer(other);

        public override IEnumerator OnStateExit()
        {
            if (self.roarAttackInProgress)
                self.StopCoroutine(self.RoarAndRunCoroutine());

            self.SetAgentSpeedAndAnimations(Speed.Walking);

            yield break;
        }
    }

    #endregion
    #region Player Transitions

    private class InvestigatePlayerTransition : AIStateTransition
    {
        public override bool CanTransitionBeTaken()
        {
            if (self.activeState is AttackPlayerState)
                return false;

            var player = self.CheckLineOfSightForPlayer(45, 30, 15);
            if (player == null)
            {
                player = self.PlayerHeardFromNoise;
                if (player == null)
                    return false;
            }
            else if (player.isInHangarShipRoom)
            {
                // If player is in the ship, we don't want to sense them through proximity.
                // This is because 682 climbs on top of the ship, and in the process would
                // be so close to a player inside the ship that it senses them.
                player = self.CheckLineOfSightForPlayer(45, 30, -1);
                if (player == null)
                    return false;
            }

            return true;
        }

        public override AIBehaviorState NextState() => new InvestigatePlayerState();
    }

    private class AttackPlayerTransition : AIStateTransition
    {
        float aggressionTimer = 10f;

        public override bool CanTransitionBeTaken()
        {
            aggressionTimer -= Time.deltaTime;

            if (aggressionTimer > 0)
                return false;

            PlayerControllerB playerInSight = self.CheckLineOfSightForPlayer(45, 30, 15);
            if (playerInSight == null)
                return false;

            self.PlayVoice(SFX.VoiceCode.Useless_ChasingPlayerForSomeTime);

            return true;
        }

        public override AIBehaviorState NextState() => new AttackPlayerState();
    }

    private class LostPlayerTransition : AIStateTransition
    {
        const float defaultPlayerLostTimer = 9;
        float playerLostTimer = defaultPlayerLostTimer;

        public override bool CanTransitionBeTaken()
        {
            PlayerControllerB playerInSight = self.CheckLineOfSightForPlayer(45, 30, 15);
            if (playerInSight != null)
            {
                playerLostTimer = defaultPlayerLostTimer;
                return false;
            }

            playerLostTimer -= Time.deltaTime;
            if (playerLostTimer <= 0)
            {
                self.PlayVoice(SFX.VoiceCode.Cowards_LostPlayer);
                return true;
            }

            return false;
        }

        public override AIBehaviorState NextState()
        {
            if (self.isOutside)
                return new WanderThroughEntranceState();
            else
                return new AtFacilityWanderingState();
        }
    }

    #endregion
    #region General Methods

    public void SetAgentSpeedAndAnimations(Speed speed)
    {
        agent.speed = (int)speed;

        if (speed == Speed.Stopped)
        {
            AnimatorSetBool(Anim.isRunning, false);
            AnimatorSetBool(Anim.isMoving, false);
            return;
        }

        AnimatorSetBool(Anim.isMoving, true);

        bool newIsRunning = speed == Speed.Running;
        if (creatureAnimator.GetBool(Anim.isRunning) != newIsRunning)
            AnimatorSetBool(Anim.isRunning, newIsRunning);
    }

    internal void DoRoarShockwaveAttackIfCan(Speed speedAfterAttack)
    {
        if (targetPlayer == null)
            return;

        if (!self.roarAttackInProgress && self.PlayerWithinRange(targetPlayer, 10, false))
            self.StartCoroutine(self.RoarShockwaveAttack(speedAfterAttack));
    }

    internal IEnumerator RoarAndRunCoroutine()
    {
        // self.SetAgentSpeedAndAnimations(Speed.Stopped); // We need Anim.isRunning to be enabled, but we must not move because of things our Animator does
        agent.speed = 0;
        AnimatorSetBool(Anim.isMoving, true);
        AnimatorSetBool(Anim.isRunning, true);

        yield return new WaitForSeconds(2.1f);
        // creatureSFX.PlayOneShot(SFX.roar.FromRandom(enemyRandom));
        // yield return new WaitForSeconds(1.1f);

        SetAgentSpeedAndAnimations(Speed.Running);
    }

    internal IEnumerator RoarShockwaveAttack(Speed speedAfterAttack)
    {
        if (roarAttackInProgress)
        {
            PLog.LogWarning($"Called {nameof(RoarShockwaveAttack)} even when a roar attack was in progress!");
            yield break;
        }
        roarAttackInProgress = true;

        agent.isStopped = true;
        self.AnimatorSetTriggerServerRpc(Anim.doRoar);
        yield return new WaitForSeconds(0.6f);

        DealDamageFromShockwaveClientRpc();
        yield return new WaitForSeconds(1.1f);

        agent.isStopped = false;
        yield return new WaitForSeconds(4f);

        roarAttackInProgress = false;
    }

    [ClientRpc]
    private void DealDamageFromShockwaveClientRpc()
    {
        PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
        if (self.PlayerWithinRange(localPlayer, 10, false))
        {
            localPlayer.DamagePlayer(5, true, true, CauseOfDeath.Blast);
            HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);

            Vector3 force = localPlayer.transform.position - agent.transform.position;
            StartCoroutine(AddForceToPlayer(localPlayer, force.normalized * 10));
        }
    }

    IEnumerator AddForceToPlayer(PlayerControllerB player, Vector3 force)
    {
        Rigidbody rb = player.playerRigidbody;
        rb.isKinematic = false;
        rb.velocity = Vector3.zero;
        player.externalForceAutoFade += force;

        yield return new WaitForSeconds(0.5f);
        yield return new WaitUntil(() => player.thisController.isGrounded || player.isInHangarShipRoom);

        rb.isKinematic = true;
    }

    internal void AttackCollideWithPlayer(Collider other)
    {
        if (attackCooldown > 0)
            return;

        if (!self.TryGetValidPlayerFromCollision(other, out var player))
            return;

        self.StartCoroutine(WaitAndDealDamage(player));
        attackCooldown = defaultAttackCooldown;
    }

    private IEnumerator WaitAndDealDamage(PlayerControllerB player)
    {
        creatureSFX.PlayOneShot(SFX.bite.FromRandom(enemyRandom));
        yield return new WaitForSeconds(0.8f);

        AnimatorSetTrigger(Anim.doBite, true);
        yield return new WaitForSeconds(0.2f);

        // TODO: This check doesn't seem to work properly, at least the scale.
        // Player just doesn't seem to get hit easily.
        // if (!IsPlayerInsideCollider(player, mainCollider, colliderScale: 100f))
        // {
        //     PLog.Log("Missed bite!");
        //     yield break;
        // }

        // Deal enough damage to set health to 30, doing 15 damage at minimum.
        // Examples: 100 => 30, 45 => 30, 40 => 25, 30 => 15.
        int damageToDeal;
        if (player.health <= 30 + 15)
            damageToDeal = 15;
        else
            damageToDeal = player.health - 30; // Set health to 30.
        player.DamagePlayer(damageToDeal);
        Vector3 force = player.transform.position - agent.transform.position;
        StartCoroutine(AddForceToPlayer(player, force.normalized * 10));


        if (player.health <= 0)
            creatureVoice.PlayOneShot(SFX.Voice.Disgusting_KilledPlayer);
    }

    internal void PlayVoice(SFX.VoiceCode clipCode)
    {
        if (!IsOwner)
            return;

        PlayVoiceServerRpc(clipCode);
    }

    [ServerRpc]
    void PlayVoiceServerRpc(SFX.VoiceCode clipCode) =>
        PlayVoiceClientRpc(clipCode);

    [ClientRpc]
    void PlayVoiceClientRpc(SFX.VoiceCode clipCode)
    {
        if (!Plugin.BoundConfig.doSpeaking.Value)
            return;

        AudioClip audioClip = SFX.Voice.GetClip(clipCode);
        creatureVoice.PlayOneShot(audioClip);
    }


    internal IEnumerator PlayVoiceInSeconds(SFX.VoiceCode clipCode, float seconds)
    {
        if (!Plugin.BoundConfig.doSpeaking.Value)
            yield break;

        yield return new WaitForSeconds(seconds);
        PlayVoice(clipCode);
    }

    internal IEnumerator ChangeEnemyScaleTo(EnemyScale enemyScale)
    {
        float targetScale = GetTargetScale(enemyScale);

        if (transform.localScale.x + 0.1f > targetScale)
        {
            // Shrink.
            while (transform.localScale.x - 0.1f > targetScale)
            {
                float nextScale = Mathf.Lerp(transform.localScale.x, targetScale, Time.deltaTime);
                transform.localScale = new Vector3(nextScale, nextScale, nextScale);
                // PLog.Log("Shrinking: " + transform.localScale.x);
                yield return null;
            }
        }
        if (transform.localScale.x - 0.1f < targetScale)
        {
            // Grow.
            while (transform.localScale.x + 0.1f < targetScale)
            {
                float nextScale = Mathf.Lerp(transform.localScale.x, targetScale, Time.deltaTime);
                transform.localScale = new Vector3(nextScale, nextScale, nextScale);
                // PLog.Log("Growing: " + transform.localScale.x);
                yield return null;
            }
        }
    }

    private static float GetTargetScale(EnemyScale enemyScale) => enemyScale switch
    {
        // This is stupid code.
        EnemyScale.Small => 2.3f,
        EnemyScale.Big => 4f,
        _ => throw new ArgumentOutOfRangeException("invalid scale value")
    };

    internal bool TryTargetEnemyInProximity([NotNullWhen(returnValue: true)] out MonoBehaviour? enemy)
    {
        foreach (Collider enemyCollider in GetEnemiesInProximity())
        {
            if (enemyCollider.TryGetComponent<EnemyAI>(out var enemyAI))
            {
                DebugLog($"Found enemy {enemyAI.name}");

                if (!enemyAI.enemyType.canDie)
                {
                    // DebugLog("... but the enemy can't die!");
                    continue;
                }

                if (enemyAI.isEnemyDead)
                {
                    // DebugLog("... but the enemy is already dead!");
                    continue;
                }

                if (enemyAI is SCP682AI)
                    continue;

                if (BlackListedEnemiesContains(enemyAI))
                    continue;

                if (!agent.CalculatePath(enemyAI.agent.transform.position, tempPath))
                {
                    // DebugLog("... but can't pathfind to the enemy!");
                    continue;
                }

                enemy = enemyAI;
                DebugLog($"Found enemy {enemy.name} to target!");
                return true;
            }
        }

        foreach (CaveDwellerAI caveDweller in GetBabyCaveDwellersInProximity())
        {
            if (!agent.CalculatePath(caveDweller.agent.transform.position, tempPath))
                continue;

            if (BlackListedEnemiesContains(caveDweller))
                    continue;

            DebugLog($"Found cave dweller to target!");
            enemy = caveDweller;
            return true;
        }

        enemy = null;
        return false;
    }

    internal IEnumerable<Collider> GetEnemiesInProximity()
    {
        int collisionsAmount = Physics.OverlapSphereNonAlloc(eye.position, 40f, SCP682AI.tempCollisionArr, SCP682AI.visibleThreatsMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < collisionsAmount; i++)
        {
            Collider collided = SCP682AI.tempCollisionArr[i];

            if (collided == mainCollider)
                continue;

            yield return collided;
        }
    }

    /// <summary>
    /// CaveDwellers can't be detected by the overlap sphere check, hence this exists.
    /// </summary>
    internal IEnumerable<CaveDwellerAI> GetBabyCaveDwellersInProximity()
    {
        foreach (CaveDwellerAI caveDweller in CaveDwellerList.GetBabyCaveDwellers())
        {
            if (Vector3.Distance(eye.position, caveDweller.transform.position) <= 40f)
                yield return caveDweller;
        }
    }

    bool BlackListedEnemiesContains(MonoBehaviour enemy)
    {
        for (int i = 0; i < blacklistedEnemies.Count; i++)
        {
            var blacklistedEnemy = blacklistedEnemies[i];
            if (blacklistedEnemy == null)
            {
                blacklistedEnemies.RemoveAt(i);
                i--;
                continue;
            }

            if (blacklistedEnemy == enemy)
                return true;
        }

        return false;
    }

    // I could have used NetworkAnimator, but thought I might as well just do this
    // because I know this should work.

    public void AnimatorSetBool(string name, bool value)
    {
        if (!IsOwner)
            return;

        AnimatorSetBoolServerRpc(name, value);
    }

    [ServerRpc]
    public void AnimatorSetBoolServerRpc(string name, bool value) =>
        AnimatorSetBoolClientRpc(name, value);

    [ClientRpc]
    private void AnimatorSetBoolClientRpc(string name, bool value) =>
        creatureAnimator.SetBool(name, value);

    public void AnimatorSetTrigger(string name, bool allowFromNonOwner = false)
    {
        if (!allowFromNonOwner && !IsOwner)
            return;

        AnimatorSetTriggerServerRpc(name);
    }

    [ServerRpc(RequireOwnership = false)]
    public void AnimatorSetTriggerServerRpc(string name) =>
        AnimatorSetTriggerClientRpc(name);

    [ClientRpc]
    private void AnimatorSetTriggerClientRpc(string name) =>
        creatureAnimator.SetTrigger(name);

    #endregion
    #region Debug Stuff
#if DEBUG
    public static IEnumerator DrawPath(LineRenderer line, NavMeshAgent agent)
    {
        if (!agent.enabled)
            yield break;
        yield return new WaitForEndOfFrame();
        line.SetPosition(0, agent.transform.position); //set the line's origin

        line.positionCount = agent.path.corners.Length; //set the array of positions to the amount of corners
        for (var i = 1; i < agent.path.corners.Length; i++)
        {
            line.SetPosition(i, agent.path.corners[i]); //go through each corner and set that to the line renderer's position
        }
    }
#endif
    #endregion
}
