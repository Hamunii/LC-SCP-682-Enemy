using System;
using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using SCP682.Hooks;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.SCPEnemy;

class SCP682AI : ModEnemyAI<SCP682AI>
{
    #region Initialization
    // We use this list to destroy loaded game objects when plugin is reloaded
    internal static List<GameObject> SCP682Objects = [];

    public enum Speed
    {
        Stopped = 0,
        Walking = 3,
        Running = 7
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
        internal const string doKillEnemy = "KillEnemy"; // base game thing, gets called automatically
        internal const string isMoving = "isMoving";
        internal const string isMovingInverted = "isMovingInverted";
        internal const string isRunning = "isRunning";
        internal const string isOnShip = "isOnShip";
        internal const string doBite = "doBite";
    }

    const float defaultBoredOfWanderingFacilityTimer = 120f;
    float boredOfWanderingFacilityTimer = defaultBoredOfWanderingFacilityTimer;
    Vector3 posOnTopOfShip;
    JesterAI? targetJester = null!;
    LineRenderer lineRenderer = null!;
    BoxCollider mainCollider = null!;

    internal Transform turnCompass = null!;
    internal Transform crocodileModel = null!;
    const float defaultAttackCooldown = 5f;
    float attackCooldown = defaultAttackCooldown;

    private int _defaultHealth;


#if DEBUG
    const bool IS_DEBUG_BEHAVIOR = true;
#else
    const bool IS_DEBUG_BEHAVIOR = false;
#endif

    internal override SCP682AI GetThis() => this;
    internal override AIBehaviorState GetInitialState() => new WanderToShipState();

    public override void Start()
    {
        SCP682Objects.Add(gameObject);

        _defaultHealth = enemyHP;

        posOnTopOfShip = StartOfRound.Instance.insideShipPositions[0].position + new Vector3(-2, 5, 3); // temporary
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        mainCollider = gameObject.GetComponentInChildren<BoxCollider>();

        var scale = 4f;
        crocodileModel.localScale = new(scale, scale, scale);

        if (enemyType.isOutsideEnemy)
            StartCoroutine(ChangeEnemyScaleTo(EnemyScale.Big));
        else
            StartCoroutine(ChangeEnemyScaleTo(EnemyScale.Small));

        if (IS_DEBUG_BEHAVIOR)
        {
            printDebugs = true;
            enemyType.isOutsideEnemy = !GameNetworkManager
                .Instance
                .localPlayerController
                .isInsideFactory;
            myValidState = GetPlayerState(GameNetworkManager.Instance.localPlayerController);
        }

        // creatureSFX.clip = SFX.walk.FromRandom(enemyRandom);
        // creatureSFX.loop = true;
        // creatureSFX.Play();
        // agent.radius = 0.5f;
        base.Start();

        // creatureSFX.PlayOneShot(SFX.spawn.FromRandom(enemyRandom));

#if DEBUG
        if (ModMenuAPICompatibility.Enabled)
            ModMenuAPICompatibility.InitDebug(this);
        else
            PLog.LogWarning("Hamunii.ModMenuAPI not installed, debug UI can't be shown!");
#endif
    }

    public override void Update()
    {
        attackCooldown -= Time.deltaTime;
        base.Update();
    }

#if DEBUG
    public override void DoAIInterval()
    {
        base.DoAIInterval();
        StartCoroutine(DrawPath(lineRenderer, agent));
    }
#endif

    public override void HitEnemy(
        int force = 1,
        PlayerControllerB? playerWhoHit = null,
        bool playHitSFX = false,
        int hitID = -1
    )
    {
        base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        if (isEnemyDead)
            return;

        if (enemyHP > 0)
            creatureSFX.PlayOneShot(SFX.hit.FromRandom(enemyRandom));

        enemyHP -= force;
        if (enemyHP <= 0 && !isEnemyDead)
        {
            // This enemy never dies for real.
            // KillEnemyOnOwnerClient();

            OverrideState(new DeadTemporarilyState());
            return;
        }

        if (activeState is not AttackPlayerState)
            OverrideState(new AttackPlayerState());
    }

    #endregion
    #region Outside States

    private class WanderToShipState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new OnShipAmbushState.ArrivedAtShipTransition(), new InvestigatePlayerTransition()];

        public override IEnumerator OnStateEntered()
        {
            CreatureAnimator.SetBool(Anim.isMoving, true);
            yield break;
        }

        public override void AIInterval()
        {
            self.SetDestinationToPosition(self.posOnTopOfShip);
        }

        public override IEnumerator OnStateExit() { yield break; }
    }

    private class OnShipAmbushState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AmbushPlayerFromShipTransition(), new BoredOfAmbushTransition()];

        public override IEnumerator OnStateEntered()
        {
            CreatureAnimator.SetBool(Anim.isMoving, false);
            CreatureAnimator.SetBool(Anim.isOnShip, true);

            // Make sure we aren't still pathfinding anywhere else
            // this probably doesn't even work, but it's for testing anyways
            Agent.SetDestination(Agent.transform.position);
            self.destination = Agent.transform.position;

            Agent.enabled = false;

            self.gameObject.transform.position = self.posOnTopOfShip;

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
            CreatureAnimator.SetBool(Anim.isMoving, true);
            CreatureAnimator.SetBool(Anim.isOnShip, false);

            Agent.enabled = true;

            self.StartCoroutine(self.ChangeEnemyScaleTo(EnemyScale.Big));
            yield break;
        }

        internal class ArrivedAtShipTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                if (Vector3.Distance(self.posOnTopOfShip, self.gameObject.transform.position) < 2)
                    return true;
                return false;
            }

            public override AIBehaviorState NextState() => new OnShipAmbushState();
        }

        private class AmbushPlayerFromShipTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                TargetPlayer = self.FindNearestPlayer();
                // TODO: make it better
                if (self.PlayerWithinRange(TargetPlayer, 15))
                    return true;
                return false;
            }

            public override AIBehaviorState NextState() => new FromAmbushJumpPlayerState();
        }

        private class BoredOfAmbushTransition : AIStateTransition
        {
            float boredOfAmbushTimer = 20f;

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
                Plugin.Logger.LogError("Trying to ambush player, but targetPlayer is null! Attacking player instead.");
                self.OverrideState(new AttackPlayerState());
                yield break;
            }

            Vector3 positionBehindPlayer;

            {
                Vector3 targetPositionBehindPlayer = TargetPlayer.transform.position - Vector3.Scale(new Vector3(5, 0, 5), TargetPlayer.transform.forward);
                
                if (!NavMesh.SamplePosition(targetPositionBehindPlayer, out NavMeshHit navHit, maxDistance: 10f, NavMesh.AllAreas))
                {
                    Plugin.Logger.LogWarning("Trying to ambush player, but didn't find NavMesh near target player! Attacking player instead.");
                    self.OverrideState(new AttackPlayerState());
                    yield break;
                }

                positionBehindPlayer = navHit.position;
            }

            Vector3 positionInBetweenInAir = (positionBehindPlayer + positionBehindPlayer) / 2 + (Vector3.up * 5f);
            Vector3 originalPosition = self.transform.position;

            self.SetAgentSpeedAndAnimations(Speed.Stopped);
            Agent.enabled = false;

            // Everything is now validated and set up, we can perform the jump animation.
            float normalizedTimer = 0f;
            while (normalizedTimer <= 1f)
            {
                float scaledDeltaTime = Time.deltaTime * 0.5f;
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
        public override IEnumerator OnStateEntered()
        {
            TargetPlayer = self.FindNearestPlayer();
            self.EnterSpecialAnimationWithPlayer(TargetPlayer, stopMovementCalculations: false);

            facilityEntrance = RoundManager.FindMainEntranceScript(self.isOutside);
            if (facilityEntrance == null)
            {
                PLog.LogError("Can't pathfind to entrance because it doesn't exist.");
                yield break;
            }

            if (!self.SetDestinationToPosition(facilityEntrance.entrancePoint.position, true))
                PLog.LogError("Facility door is unreachable!");
            yield break;
        }

        public override void AIInterval()
        {
            if (!self.SetDestinationToPosition(facilityEntrance.entrancePoint.position, true))
                PLog.LogError("Facility door is unreachable!");
        }

        // Works better than LateUpdate, that one fucks up the helmet overlay thingy position
        public override void Update()
        {
            if (self.inSpecialAnimationWithPlayer.inAnimationWithEnemy != self)
            {
                Plugin.Logger.LogWarning("Player is no longer in special animation with this enemy!");
                self.OverrideState(new AttackPlayerState());
                return;
            }

            if (self.inSpecialAnimationWithPlayer != GameNetworkManager.Instance.localPlayerController)
                return;

            self.inSpecialAnimationWithPlayer.transform.position =
                CreatureVoice.transform.position + new Vector3(0, -1f, 0); // creatureVoice is positioned in the mouth
        }

        public override IEnumerator OnStateExit()
        {
            self.CancelSpecialAnimationWithPlayer();
            yield break;
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
            [new InvestigatePlayerTransition()];
        EntranceTeleport facilityEntrance = null!;

        public override IEnumerator OnStateEntered()
        {
            CreatureAnimator.SetBool(Anim.isMoving, true);

            facilityEntrance = RoundManager.FindMainEntranceScript(self.isOutside);
            Transitions.Add(new EnterEntranceTransition());
            yield break;
        }

        public override void AIInterval()
        {
            // TODO: More interesting pathing
            if (!self.SetDestinationToPosition(facilityEntrance.entrancePoint.position, true)) // when checkForPath is true, pathfinding is a little better (can path to the half-obstructed door in test level)
                PLog.LogWarning("Facility door is unreachable!");
            
        }

        public override IEnumerator OnStateExit()
        {
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
                if (Vector3.Distance(_et.entrancePoint.position, self.gameObject.transform.position) < 3)
                {
                    self.TeleportSelfToOtherEntranceClientRpc(self.isOutside);
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
                new InvestigatePlayerTransition()
            ];

        public override IEnumerator OnStateEntered()
        {
            CreatureAnimator.SetBool(Anim.isMoving, true);

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
                self.boredOfWanderingFacilityTimer -= Time.deltaTime;
                if (debugMSGTimer - self.boredOfWanderingFacilityTimer > 10)
                {
                    debugMSGTimer = self.boredOfWanderingFacilityTimer;
                    self.DebugLog(
                        $"[{nameof(BoredOfFacilityTransition)}] Time until bored: {self.boredOfWanderingFacilityTimer}"
                    );
                }
                if (self.boredOfWanderingFacilityTimer <= 0)
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

        public override IEnumerator OnStateEntered()
        {
            CreatureAnimator.SetBool(Anim.isMoving, true);
            self.SetAgentSpeedAndAnimations(Speed.Running);
            yield break;
        }

        public override void AIInterval()
        {
            if (self.targetJester is null)
                return;

            var jesterPos = self.targetJester.agent.transform.position;

            if (Vector3.Distance(jesterPos, self.transform.position) < 3)
                self.targetJester.KillEnemy(true);

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

                for (int i = 0; i < JesterListHook.jesterEnemies.Count; i++)
                {
                    var jester = JesterListHook.jesterEnemies[i];
                    if (jester is null)
                    {
                        JesterListHook.jesterEnemies.RemoveAt(i);
                        i--;
                        continue;
                    }
                    if (jester.farAudio.isPlaying)
                    {
                        self.targetJester = jester;
                        JesterListHook.jesterEnemies.RemoveAt(i);
                        return true;
                    }
                }
                return false;
            }

            public override AIBehaviorState NextState() => new AtFacilityEatNoisyJesterState();
        }

        private class NoisyJesterEatenTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken() => self.targetJester is null;

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

            Agent.enabled = false;
            CreatureAnimator.SetTrigger(Anim.doKillEnemy);
            yield break;
        }

        public override IEnumerator OnStateExit()
        {
            self.enemyHP = self._defaultHealth;

            Agent.enabled = true;

            // Reset the animator
            CreatureAnimator.Rebind();
            CreatureAnimator.Update(0f);

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

    #endregion
    #region Player States

    private class InvestigatePlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AttackPlayerTransition(), new LostPlayerTransition()];

        public override IEnumerator OnStateEntered()
        {
            CreatureAnimator.SetBool(Anim.isMoving, true);
            yield break;
        }

        public override void AIInterval()
        {
            TargetPlayer = self.FindNearestPlayer();
            self.SetDestinationToPosition(TargetPlayer.transform.position);
        }

        public override void OnCollideWithPlayer(Collider other) =>
            self.AttackCollideWithPlayer(other);

        public override IEnumerator OnStateExit() { yield break; }
    }

    private class AttackPlayerState : AIBehaviorState
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
            self.SetDestinationToPosition(TargetPlayer.transform.position);
        }

        public override void OnCollideWithPlayer(Collider other) =>
            self.AttackCollideWithPlayer(other);

        public override IEnumerator OnStateExit()
        {
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

            if (self.CheckLineOfSightForPlayer(45, 60, 6))
                return true;
            return false;
        }

        public override AIBehaviorState NextState() => new InvestigatePlayerState();
    }

    private class AttackPlayerTransition : AIStateTransition
    {
        float aggressionTimer = 10f;

        public override bool CanTransitionBeTaken()
        {
            aggressionTimer -= Time.deltaTime;
            if (aggressionTimer <= 0)
                return true;
            return false;
        }

        public override AIBehaviorState NextState() => new AttackPlayerState();
    }

    private class LostPlayerTransition : AIStateTransition
    {
        const float defaultPlayerLostTimer = 5;
        float playerLostTimer = defaultPlayerLostTimer;

        public override bool CanTransitionBeTaken()
        {
            var playerInSight = self.CheckLineOfSightForPlayer(45, 20, 6);
            if (playerInSight is not null)
            {
                playerLostTimer = defaultPlayerLostTimer;
                return false;
            }

            playerLostTimer -= Time.deltaTime;
            if (playerLostTimer <= 0)
                return true;
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
            creatureAnimator.SetBool(Anim.isRunning, false);
            creatureAnimator.SetBool(Anim.isMoving, false);
            return;
        }

        creatureAnimator.SetBool(Anim.isMoving, true);

        bool newIsRunning = speed == Speed.Running;
        if (creatureAnimator.GetBool(Anim.isRunning) != newIsRunning)
        {
            creatureAnimator.SetBool(Anim.isRunning, newIsRunning);

            if (speed == Speed.Walking)
                creatureSFX.clip = SFX.walk.FromRandom(enemyRandom);
            else
                creatureSFX.clip = SFX.run.FromRandom(enemyRandom);
        }
    }

    internal IEnumerator RoarAndRunCoroutine()
    {
        // self.SetAgentSpeedAndAnimations(Speed.Stopped); // We need Anim.isRunning to be enabled, but we must not move because of things our Animator does
        agent.speed = 0;
        creatureAnimator.SetBool(Anim.isMoving, true);
        creatureAnimator.SetBool(Anim.isRunning, true);

        yield return new WaitForSeconds(1);
        creatureSFX.PlayOneShot(SFX.roar.FromRandom(enemyRandom));
        yield return new WaitForSeconds(1.5f);

        SetAgentSpeedAndAnimations(Speed.Running);
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

        creatureAnimator.SetTrigger(Anim.doBite);
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
    }

    internal IEnumerator ChangeEnemyScaleTo(EnemyScale enemyScale)
    {
        float targetScale = GetTargetScale(enemyScale);

        if (targetScale < transform.localScale.x)
        {
            // Shrink.
            while (transform.localScale.x > targetScale)
            {
                float nextScale = Mathf.Lerp(transform.localScale.x, targetScale, Time.deltaTime);
                transform.localScale = new Vector3(nextScale, nextScale, nextScale);
                yield return null;
            }
        }
        if (targetScale > transform.localScale.x)
        {
            // Grow.
            while (transform.localScale.x < targetScale)
            {
                float nextScale = Mathf.Lerp(transform.localScale.x, targetScale, Time.deltaTime);
                transform.localScale = new Vector3(nextScale, nextScale, nextScale);
                yield return null;
            }
        }
    }

    private static float GetTargetScale(EnemyScale enemyScale) => enemyScale switch
    {
        // This is stupid code.
        EnemyScale.Small => 4f,
        EnemyScale.Big => 5.5f,
        _ => throw new ArgumentOutOfRangeException("invalid scale value")
    };

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
