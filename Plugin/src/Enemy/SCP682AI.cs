using System;
using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using ModMenuAPI.ModMenuItems;
using SCP682.Hooks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SCP682.SCPEnemy;

class SCP682AI : ModEnemyAI<SCP682AI>
{
    // We use this list to destroy loaded game objects when plugin is reloaded
    internal static List<GameObject> SCP682Objects = [];

    public enum Speed
    {
        Walking = 3,
        Running = 7
    }

    public void SetAgentSpeed(Speed speed)
    {
        agent.speed = (int)speed;

        bool newIsRunning = speed == Speed.Running;
        if (creatureAnimator.GetBool(Anim.isRunning) != newIsRunning)
            creatureAnimator.SetBool(Anim.isRunning, newIsRunning);
    }

    const float defaultBoredOfWanderingFacilityTimer = 120f;
    float boredOfWanderingFacilityTimer = defaultBoredOfWanderingFacilityTimer;
    bool readyToMakeTransitionFromAmbush = true;
    Vector3 posOnTopOfShip;
    JesterAI? targetJester = null!;
    LineRenderer lineRenderer = null!;

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
        // agent.radius = 0.5f;
        base.Start();

        InitDebug();
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
        if (ActiveState is AttackPlayerState state)
            state.AttackCollideWithPlayer(other);
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

        public override void OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
        }

        public override void AIInterval()
        {
            self.SetDestinationToPosition(self.posOnTopOfShip);
        }

        public override void OnStateExit() { }
    }

    private class OnShipAmbushState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AmbushPlayerFromShipTransition(), new BoredOfAmbushTransition()];

        public override void OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, false);
            creatureAnimator.SetBool(Anim.isOnShip, true);

            agent.enabled = false;
            // TODO: Jump animation
            self.gameObject.transform.position = self.posOnTopOfShip;
            self.readyToMakeTransitionFromAmbush = false;
        }

        public override void OnStateExit()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
            creatureAnimator.SetBool(Anim.isOnShip, false);

            agent.enabled = true;

            self.readyToMakeTransitionFromAmbush = true;
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
                if (self.TargetPlayerWithinRange(15) && self.readyToMakeTransitionFromAmbush)
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
                if (boredOfAmbushTimer <= 0 && self.readyToMakeTransitionFromAmbush)
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

        public override void OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);

            facilityEntrance = RoundManager.FindMainEntranceScript(self.isOutside);
            Transitions.Add(new EnterEntranceTransition());
        }

        public override void AIInterval()
        {
            // TODO: More interesting pathing
            self.SetDestinationToPosition(facilityEntrance.entrancePoint.position);
        }

        public override void OnStateExit()
        {
            self.TeleportSelfToOtherEntranceClientRpc(!self.isOutside);
        }

        public class EnterEntranceTransition : AIStateTransition
        {
            EntranceTeleport? _et;

            public override bool CanTransitionBeTaken()
            {
                _et ??= RoundManager.FindMainEntranceScript(self.isOutside);
                if (
                    Vector3.Distance(_et.entrancePoint.position, self.gameObject.transform.position)
                    < 3
                )
                    return true;
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

        public override void OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);

            self.StartSearch(self.transform.position);
        }

        public override void OnStateExit()
        {
            self.StopSearch(self.currentSearch);
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

        public override void OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
            self.SetAgentSpeed(Speed.Running);
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

        public override void OnStateExit()
        {
            self.SetAgentSpeed(Speed.Walking);
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

        public override void OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
        }

        public override void AIInterval()
        {
            self.targetPlayer = self.FindNearestPlayer();
            self.SetDestinationToPosition(self.targetPlayer.transform.position);
        }

        public override void OnStateExit() { }
    }

    private class AttackPlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new LostPlayerTransition()];

        const float defaultCooldown = 0.5f;
        float attackCooldown = defaultCooldown;

        public override void OnStateEntered()
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
            self.SetAgentSpeed(Speed.Running);
        }

        public override void UpdateBehavior() => attackCooldown -= Time.deltaTime;

        public override void AIInterval()
        {
            self.targetPlayer = self.FindNearestPlayer();
            self.SetDestinationToPosition(self.targetPlayer.transform.position);
        }

        public override void OnStateExit()
        {
            self.SetAgentSpeed(Speed.Walking);
        }

        internal void AttackCollideWithPlayer(Collider other)
        {
            if (attackCooldown > 0)
                return;

            PlayerControllerB? player = self.MeetsStandardPlayerCollisionConditions(other);
            if (player is not null)
            {
                int damageToDeal;
                if (player.health > 45) // At min do 15 damage
                    damageToDeal = player.health + 30; // Set health to 30
                else
                    damageToDeal = 15;
                player.DamagePlayer(damageToDeal);
                self.creatureAnimator.SetTrigger(Anim.doBite);

                attackCooldown = defaultCooldown;
            }
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

            if (self.CheckLineOfSightForPlayer())
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
            var playerInSight = self.CheckLineOfSightForPlayer();
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

    MMButtonMenuInstantiable mmMenu = new("Override State >");

    public void InitDebug()
    {
        new ModMenu("SCP-682 Debug")
            .RegisterItem(new DebugNewSearchRoutineAction(this))
            .RegisterItem(mmMenu)
            .RegisterItem(new DebugPrintInfoAction(this));

        mmMenu.MenuItems.Add(new DebugOverrideState(this, typeof(WanderToShipState)));
        mmMenu.MenuItems.Add(new DebugOverrideState(this, typeof(OnShipAmbushState)));
        mmMenu.MenuItems.Add(new DebugOverrideState(this, typeof(WanderThroughEntranceState)));
        mmMenu.MenuItems.Add(new DebugOverrideState(this, typeof(AtFacilityWanderingState)));
    }

    class DebugPrintInfoAction(SCP682AI self) : MMButtonAction("Print Info")
    {
        protected override void OnClick()
        {
            P.Log($"Current State: {self.ActiveState}");
        }
    }

    class DebugNewSearchRoutineAction(SCP682AI self) : MMButtonAction("New Search Routine")
    {
        protected override void OnClick() => self.StartSearch(self.transform.position);
    }

    class DebugOverrideState(SCP682AI self, Type state) : MMButtonAction($"{state.Name}")
    {
        protected override void OnClick()
        {
            var stateInstance = (AIBehaviorState)Activator.CreateInstance(state);
            self.OverrideState(stateInstance);
        }
    }

#endif
    #endregion
}
