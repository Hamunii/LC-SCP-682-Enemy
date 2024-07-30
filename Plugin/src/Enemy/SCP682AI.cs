using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using SCP682.Hooks;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.SCPEnemy;

class SCP682AI : ModEnemyAI<SCP682AI>
{
    // We use this list to destroy loaded game objects when plugin is reloaded
    internal static List<GameObject> SCP682Objects = [];

    public enum Speed
    {
        Stopped = 0,
        Walking = 3,
        Running = 7
    }

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

    const float defaultBoredOfWanderingFacilityTimer = 120f;
    float boredOfWanderingFacilityTimer = defaultBoredOfWanderingFacilityTimer;
    Vector3 posOnTopOfShip;
    JesterAI? targetJester = null!;
    LineRenderer lineRenderer = null!;

    const float defaultAttackCooldown = 5f;
    float attackCooldown = defaultAttackCooldown;

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

#if DEBUG
    const bool IS_DEBUG_BEHAVIOR = true;
#else
    const bool IS_DEBUG_BEHAVIOR = false;
#endif

    public override void Start()
    {
        posOnTopOfShip =
            StartOfRound.Instance.insideShipPositions[0].position + new Vector3(0, 5, 0); // temporary
        lineRenderer = gameObject.AddComponent<LineRenderer>();

        self = this;
        SCP682Objects.Add(gameObject);
        InitialState = new WanderToShipState();
        if (enemyType.isOutsideEnemy)
        {
            var scale = 4f;
            gameObject.transform.Find("CrocodileModel").localScale = new(scale, scale, scale);
        }
        if (IS_DEBUG_BEHAVIOR)
        {
            PrintDebugs = true;
            enemyType.isOutsideEnemy = !GameNetworkManager
                .Instance
                .localPlayerController
                .isInsideFactory;
            MyValidState = GetPlayerState(GameNetworkManager.Instance.localPlayerController);
        }

        // creatureSFX.clip = SFX.walk.FromRandom(enemyRandom);
        // creatureSFX.loop = true;
        // creatureSFX.Play();
        // agent.radius = 0.5f;
        base.Start();

        creatureSFX.PlayOneShot(SFX.spawn.FromRandom(enemyRandom));

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

    public override void OnCollideWithPlayer(Collider other)
    {
        base.OnCollideWithPlayer(other);
        if (ActiveState is AttackPlayerState or InvestigatePlayerState)
            AttackCollideWithPlayer(other);
    }

    internal void AttackCollideWithPlayer(Collider other)
    {
        if (attackCooldown > 0)
            return;

        PlayerControllerB? player = self.MeetsStandardPlayerCollisionConditions(other);
        if (player is not null)
        {
            self.StartCoroutine(WaitAndDealDamage(player));
            attackCooldown = defaultAttackCooldown;
        }
    }

    private IEnumerator WaitAndDealDamage(PlayerControllerB player)
    {
        self.creatureSFX.PlayOneShot(SFX.bite.FromRandom(enemyRandom));
        yield return new WaitForSeconds(0.8f);
        creatureAnimator.SetTrigger(Anim.doBite);
        yield return new WaitForSeconds(0.2f);
        int damageToDeal;
        if (player.health > 45) // At min do 15 damage
            damageToDeal = player.health - 30; // Set health to 30
        else
            damageToDeal = 15;
        player.DamagePlayer(damageToDeal);
    }

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
            // Our death sound will be played through creatureVoice when KillEnemy() is called.
            // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
            // so we don't need to call a death animation ourselves.

            // We need to stop our search coroutine, because the game does not do that by default.
            StopCoroutine(searchCoroutine);
            KillEnemyOnOwnerClient();
        }

        if (ActiveState is not AttackPlayerState)
            OverrideState(new AttackPlayerState());
    }

    #region Outside States

    private class WanderToShipState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new OnShipAmbushState.ArrivedAtShipTransition(), new InvestigatePlayerTransition()];

        public override IEnumerator OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
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
            creatureAnimator.SetBool(Anim.isMoving, false);
            creatureAnimator.SetBool(Anim.isOnShip, true);

            agent.enabled = false;
            // TODO: Jump animation
            self.gameObject.transform.position = self.posOnTopOfShip;
            yield break;
        }

        public override IEnumerator OnStateExit()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
            creatureAnimator.SetBool(Anim.isOnShip, false);

            agent.enabled = true;
            yield break;
        }

        internal class ArrivedAtShipTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                if (Vector3.Distance(self.posOnTopOfShip, self.gameObject.transform.position) < 20)
                    return true;
                return false;
            }

            public override AIBehaviorState NextState() => new OnShipAmbushState();
        }

        private class AmbushPlayerFromShipTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                self.targetPlayer = self.FindNearestPlayer();
                // TODO: make it better
                if (self.TargetPlayerWithinRange(15))
                    return true;
                return false;
            }

            public override AIBehaviorState NextState() => new AttackPlayerState();
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

    private class WanderThroughEntranceState : AIBehaviorState
    {
        // Note: We add one more transition to this afterwards!
        public override List<AIStateTransition> Transitions { get; set; } =
            [new InvestigatePlayerTransition()];
        EntranceTeleport facilityEntrance = null!;

        public override IEnumerator OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);

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

        public override IEnumerator OnStateExit() { yield break; }

        public class EnterEntranceTransition : AIStateTransition
        {
            EntranceTeleport? _et;

            public override bool CanTransitionBeTaken()
            {
                _et ??= RoundManager.FindMainEntranceScript(self.isOutside);
                if (Vector3.Distance(_et.entrancePoint.position, self.gameObject.transform.position) < 3)
                {
                    self.TeleportSelfToOtherEntranceClientRpc(!self.isOutside);
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
            creatureAnimator.SetBool(Anim.isMoving, true);

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
            creatureAnimator.SetBool(Anim.isMoving, true);
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
    #region Player States

    private class InvestigatePlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AttackPlayerTransition(), new LostPlayerTransition()];

        public override IEnumerator OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
            yield break;
        }

        public override void AIInterval()
        {
            self.targetPlayer = self.FindNearestPlayer();
            self.SetDestinationToPosition(self.targetPlayer.transform.position);
        }

        public override IEnumerator OnStateExit() { yield break; }
    }

    private class AttackPlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new LostPlayerTransition()];

        public override IEnumerator OnStateEntered()
        {
            // self.SetAgentSpeedAndAnimations(Speed.Stopped); // We need Anim.isRunning to be enabled, but we must not move because of things our Animator does
            agent.speed = 0;
            creatureAnimator.SetBool(Anim.isMoving, true);
            creatureAnimator.SetBool(Anim.isRunning, true);

            yield return new WaitForSeconds(1);
            self.creatureSFX.PlayOneShot(SFX.roar.FromRandom(enemyRandom));
            yield return new WaitForSeconds(1.5f);

            self.SetAgentSpeedAndAnimations(Speed.Running);
        }

        public override void AIInterval()
        {
            self.targetPlayer = self.FindNearestPlayer();
            self.SetDestinationToPosition(self.targetPlayer.transform.position);
        }

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
            if (self.ActiveState is AttackPlayerState)
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
