using System;
using System.Collections.Generic;
using GameNetcodeStuff;
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

    #region States

    private class WanderToShipState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new OnShipAmbushState.ArrivedAtShipTransition(), new InvestigatePlayerTransition()];

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
        }

        public override void UpdateBehavior(Animator creatureAnimator) { }

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
        }

        public override void UpdateBehavior(Animator creatureAnimator) { }

        public override void OnStateExit(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
            creatureAnimator.SetBool(Anim.isOnShip, false);

            self.currentTravelDirection = TravelingTo.Facility;
        }

        internal class ArrivedAtShipTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken() => throw new NotImplementedException();

            public override AIBehaviorState NextState() => new OnShipAmbushState();
        }

        private class AmbushPlayerFromShipTransition : AIStateTransition
        {
            public override bool CanTransitionBeTaken() => throw new NotImplementedException();

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
                else
                    return false;
            }

            public override AIBehaviorState NextState() => new WanderToFacilityState();
        }
    }

    private class WanderToFacilityState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new InvestigatePlayerTransition()];

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
        }

        public override void UpdateBehavior(Animator creatureAnimator) { }

        public override void OnStateExit(Animator creatureAnimator) { }
    }

    private class AtFacilityWanderingState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new BoredOfFacilityTransition(), new InvestigatePlayerTransition()];

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
        }

        public override void UpdateBehavior(Animator creatureAnimator) { }

        public override void OnStateExit(Animator creatureAnimator) { }

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

    private class AtFacilityEatNoisyJesterState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AtFacilityNoisyJesterEaten()];

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
        }

        public override void UpdateBehavior(Animator creatureAnimator) { }

        public override void OnStateExit(Animator creatureAnimator) { }

        private class AtFacilityFindNoisyJester : AIStateTransition
        {
            public override bool CanTransitionBeTaken() => throw new NotImplementedException();

            public override AIBehaviorState NextState() => new AtFacilityEatNoisyJesterState();
        }

        private class AtFacilityNoisyJesterEaten : AIStateTransition
        {
            public override bool CanTransitionBeTaken() => throw new NotImplementedException();

            public override AIBehaviorState NextState() => new AtFacilityWanderingState();
        }
    }

    private class InvestigatePlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new AttackPlayerTransition(), new LostPlayerTransition()];

        public override void OnStateEntered(Animator creatureAnimator)
        {
            creatureAnimator.SetBool(Anim.isMoving, true);
        }

        public override void UpdateBehavior(Animator creatureAnimator) { }

        public override void OnStateExit(Animator creatureAnimator) { }
    }

    private class AttackPlayerState : AIBehaviorState
    {
        public override List<AIStateTransition> Transitions { get; set; } =
            [new LostPlayerTransition(), new InvestigatePlayerTransition()];

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
    #region Generic Transitions

    private class InvestigatePlayerTransition : AIStateTransition
    {
        public override bool CanTransitionBeTaken()
        {
            if (self.ActiveState is AttackPlayerState)
                return false;

            throw new NotImplementedException();
        }

        public override AIBehaviorState NextState() => new InvestigatePlayerState();
    }

    private class AttackPlayerTransition : AIStateTransition
    {
        public override bool CanTransitionBeTaken() => throw new NotImplementedException();

        public override AIBehaviorState NextState() => new AttackPlayerState();
    }

    private class LostPlayerTransition : AIStateTransition
    {
        public override bool CanTransitionBeTaken() => throw new NotImplementedException();

        public override AIBehaviorState NextState()
        {
            if (self.currentTravelDirection == TravelingTo.Ship)
                return new WanderToShipState();
            else
                return new WanderToFacilityState();
        }
    }

    #endregion
}
