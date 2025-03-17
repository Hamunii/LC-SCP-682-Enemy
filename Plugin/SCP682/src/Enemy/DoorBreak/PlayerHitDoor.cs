// From https://github.com/snowlance7/HeavyItemSCPs

using System.Collections;
using GameNetcodeStuff;
using UnityEngine;

namespace SCP682.SCPEnemy.DoorBreak;

internal class PlayerHitDoorCollider : MonoBehaviour
{
    readonly float doorFlyingTime = 3f;
    bool hitPlayer = false;
    bool isActive = true;
    public Vector3 force;
    int damage = 30;

    public void Start()
    {
        StartCoroutine(DisableAfterDelay());
    }

    void OnTriggerEnter(Collider other)
    {
        if (isActive && !hitPlayer && other.CompareTag("Player"))
        {
            PlayerControllerB player = other.GetComponent<PlayerControllerB>();
            if (player != GameNetworkManager.Instance.localPlayerController)
                return;
            // logger.LogDebug("Door hit player " + player.playerUsername);
            player.DamagePlayer(damage, true, true, CauseOfDeath.Inertia, 0, false, force);
            StartCoroutine(AddForceToPlayer(player));
            hitPlayer = true;
        }
    }

    IEnumerator AddForceToPlayer(PlayerControllerB player)
    {
        Rigidbody rb = player.playerRigidbody;
        rb.isKinematic = false;
        rb.velocity = Vector3.zero;
        player.externalForceAutoFade += force;

        yield return new WaitForSeconds(0.5f);
        yield return new WaitUntil(() => player.thisController.isGrounded || player.isInHangarShipRoom);

        rb.isKinematic = true;
    }

    private IEnumerator DisableAfterDelay()
    {
        yield return new WaitForSeconds(doorFlyingTime);
        isActive = false;
    }
}