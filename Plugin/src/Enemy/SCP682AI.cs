using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using SCP682.Hooks;
using Unity.Netcode;
using UnityEngine;

namespace SCP682.SCPEnemy;

class SCP682AI : ModEnemyAI<SCP682AI>
{
    // We use this list to destroy loaded game objects when plugin is reloaded
    internal static List<GameObject> SCP682Objects = [];

    enum TravelingTo
    {
        Ship,
        Facility
    }

    TravelingTo currentTravelDirection = TravelingTo.Facility;
    const float defaultBoredOfWanderingFacilityTimer = 120f;
    float boredOfWanderingFacilityTimer = defaultBoredOfWanderingFacilityTimer;
    bool readyToMakeTransitionFromAmbush = true;
    Vector3 posOnTopOfShip = StartOfRound.Instance.insideShipPositions[0].position; // temporary

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
        base.Start();
    }

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

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
        }

        public override void AIInterval(Animator creatureAnimator)
        {
            self.SetDestinationToPosition(self.posOnTopOfShip);
        }

        public override void OnStateExit(Animator creatureAnimator) { }
    }

    private class OnShipAmbushState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AmbushPlayerFromShipTransition(), new BoredOfAmbushTransition()];

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, false);
            creatureAnimator.SetBool(Anim.isOnShip, true);

            agent.enabled = false;
            // TODO: Jump animation
            self.gameObject.transform.position = self.posOnTopOfShip;
            self.readyToMakeTransitionFromAmbush = false;
        }

        public override void OnStateExit(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
            creatureAnimator.SetBool(Anim.isOnShip, false);

            agent.enabled = true;

            self.currentTravelDirection = TravelingTo.Facility;
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
                // TODO: make it better
                if (self.PlayerWithinRange(15) && self.readyToMakeTransitionFromAmbush)
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

            public override AIBehaviorState NextState() => new WanderToFacilityState();
        }
    }

    private class WanderToFacilityState : AIBehaviorState
    {
        // Note: We add one more transition to this afterwards!
        public override List<AIStateTransition> Transitions { get; set; } =
            [new InvestigatePlayerTransition()];
        EntranceTeleport facilityEntrance = null!;

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);

            facilityEntrance = self.GetCurrentEntrance(self.MyValidState);
            Transitions.Add(new EnterFacilityTransition(facilityEntrance));
        }

        public override void AIInterval(Animator creatureAnimator)
        {
            // TODO: More interesting pathing
            self.SetDestinationToPosition(facilityEntrance.entrancePoint.position);
        }

        public override void OnStateExit(Animator creatureAnimator) { }

        private class EnterFacilityTransition(EntranceTeleport entranceTeleport) : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                if (
                    Vector3.Distance(
                        entranceTeleport.entrancePoint.position,
                        self.gameObject.transform.position
                    ) < 5
                )
                    return true;
                return false;
            }

            public override AIBehaviorState NextState() => new AtFacilityWanderingState();
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

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);

            if (self.MyValidState == PlayerState.Outside)
                self.TeleportSelfToOtherEntranceClientRpc(wasInside: false);

            self.StartSearch(self.transform.position);
        }

        public override void OnStateExit(Animator creatureAnimator)
        {
            self.StopSearch(self.currentSearch);
        }

        private class BoredOfFacilityTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken()
            {
                self.boredOfWanderingFacilityTimer -= Time.deltaTime;
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
                self.currentTravelDirection = TravelingTo.Ship;
                return new WanderToShipState();
            }
        }
    }

    #endregion
    #region Inside (Eat Jester)

    private class AtFacilityEatNoisyJesterState(JesterAI targetJester) : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new NoisyJesterEatenTransition(targetJester)];

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
            creatureAnimator.SetBool(Anim.isRunning, true);
        }

        public override void AIInterval(Animator creatureAnimator)
        {
            if (targetJester is null)
                return;

            var jesterPos = targetJester.agent.transform.position;

            if (Vector3.Distance(jesterPos, self.transform.position) < 3)
                targetJester.KillEnemy(true);

            self.SetDestinationToPosition(jesterPos);
        }

        public override void OnStateExit(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isRunning, false);
        }

        internal class FindNoisyJesterTransition : AIStateTransition
        {
            JesterAI targetJester = null!;

            public override bool CanTransitionBeTaken()
            {
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
                        targetJester = jester;
                        JesterListHook.jesterEnemies.RemoveAt(i);
                        return true;
                    }
                }
                return false;
            }

            public override AIBehaviorState NextState() =>
                new AtFacilityEatNoisyJesterState(targetJester);
        }

        private class NoisyJesterEatenTransition(JesterAI targetJester) : AIStateTransition
        {
            public override bool CanTransitionBeTaken() => targetJester is null;

            public override AIBehaviorState NextState() => new AtFacilityWanderingState();
        }
    }

    #endregion
    #region Player States

    private class InvestigatePlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AttackPlayerTransition(), new LostPlayerTransition()];

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
        }

        public override void OnStateExit(Animator creatureAnimator) { }
    }

    private class AttackPlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new LostPlayerTransition()];

        const float defaultCooldown = 0.5f;
        float attackCooldown = defaultCooldown;

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
            creatureAnimator.SetBool(Anim.isRunning, true);
        }

        public override void UpdateBehavior(Animator creatureAnimator)
        {
            attackCooldown -= Time.deltaTime;
        }

        public override void OnStateExit(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isRunning, false);
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
            if (self.MyValidState == PlayerState.Outside)
                return new WanderToFacilityState();
            else
                return new AtFacilityWanderingState();
        }
    }

    #endregion
    #region Utility Methods

    public void SetLocationState(bool toOutside)
    {
        if (toOutside)
        {
            // Maybe using MyValidState was a mistake?
            // Just results in writing more code without really advantages.
            MyValidState = PlayerState.Outside;
            SetEnemyOutside(toOutside);
        }
        else
        {
            MyValidState = PlayerState.Inside;
            SetEnemyOutside(!toOutside);
        }
    }

    public EntranceTeleport GetCurrentEntrance(PlayerState enemyLocation)
    {
        var shouldFindOutsideEntrance = enemyLocation == PlayerState.Outside;
        return RoundManager.FindMainEntranceScript(shouldFindOutsideEntrance);
    }

    public EntranceTeleport GetOtherEntrance(PlayerState enemyLocation)
    {
        var shouldFindOutsideEntrance = enemyLocation == PlayerState.Inside;
        return RoundManager.FindMainEntranceScript(shouldFindOutsideEntrance);
    }

    [ClientRpc]
    public void TeleportSelfToOtherEntranceClientRpc(bool wasInside)
    {
        var enemyLocation = wasInside ? PlayerState.Inside : PlayerState.Outside;
        TeleportSelfToOtherEntrance(enemyLocation);
    }

    private void TeleportSelfToOtherEntrance(PlayerState enemyLocation)
    {
        bool isOutside = enemyLocation == PlayerState.Outside;
        var targetEntrance = GetOtherEntrance(enemyLocation);
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
        SetLocationState(!isOutside);

        PlayEntranceOpeningSound(targetEntrance);
    }

    public void PlayEntranceOpeningSound(EntranceTeleport entrance)
    {
        if (entrance.doorAudios == null || entrance.doorAudios.Length == 0)
            return;
        entrance.entrancePointAudio.PlayOneShot(entrance.doorAudios[0]);
        WalkieTalkie.TransmitOneShotAudio(entrance.entrancePointAudio, entrance.doorAudios[0]);
    }

    #endregion
}
