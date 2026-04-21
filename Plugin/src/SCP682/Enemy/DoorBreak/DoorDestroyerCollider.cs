// From https://github.com/snowlance7/HeavyItemSCPs

using UnityEngine;

namespace SCP682.SCPEnemy.DoorBreak;

class DoorDestroyerCollider : MonoBehaviour
{
    public SCP682AI AI = null!;

    DoorLock? doorLock = null;
    public bool triggering;
    float timeInTrigger = 0f;

    void OnTriggerStay(Collider other)
    {
        if (AI.activeState
            is not SCP682AI.InvestigatePlayerState
            and not SCP682AI.AttackPlayerState
            and not SCP682AI.AttackEnemyState)
            return;

        if (triggering || !other.CompareTag("InteractTrigger"))
            return;

        doorLock = other.gameObject.GetComponent<DoorLock>();
        if (doorLock == null || doorLock.isDoorOpened)
            return;

        var steelDoorObj = doorLock.transform.parent.transform.parent.gameObject;
        var doorMesh = steelDoorObj.transform.Find("DoorMesh")?.gameObject;
        if (doorMesh == null)
            return;

        timeInTrigger += Time.fixedDeltaTime;

        if (timeInTrigger <= 1f)
            return;

        triggering = true;
        timeInTrigger = 0f;
        other.tag = "Untagged";

        AI.inSpecialAnimation = true;
        StartCoroutine(AI.BashDoorAnimation(BashDoor));
    }

    void BashDoor()
    {
        // DoDamageToNearbyPlayers();

        if (doorLock == null)
        {
            AI.inSpecialAnimation = false;
            return;
        }

        float doorBashForce = 35f;

        var steelDoorObj = doorLock.transform.parent.transform.parent.gameObject;
        var doorMesh = steelDoorObj.transform.Find("DoorMesh")?.gameObject;
        if (doorMesh == null)
        {
            AI.inSpecialAnimation = false;
            return;
        }

        GameObject flyingDoorPrefab = new GameObject("FlyingDoor");
        BoxCollider tempCollider = flyingDoorPrefab.AddComponent<BoxCollider>();
        tempCollider.isTrigger = true;
        tempCollider.size = new Vector3(1f, 1.5f, 3f);

        flyingDoorPrefab.AddComponent<PlayerHitDoorCollider>();

        AudioSource tempAS = flyingDoorPrefab.AddComponent<AudioSource>();
        tempAS.spatialBlend = 1;
        tempAS.maxDistance = 60;
        tempAS.rolloffMode = AudioRolloffMode.Linear;
        tempAS.volume = 1f;

        var flyingDoor = UnityEngine.Object.Instantiate(flyingDoorPrefab, doorLock.transform.position, doorLock.transform.rotation);
        doorMesh.transform.SetParent(flyingDoor.transform);

        GameObject.Destroy(flyingDoorPrefab);

        Rigidbody rb = flyingDoor.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.useGravity = true;
        rb.isKinematic = true;

        // Determine which direction to apply the force
        Vector3 doorForward = flyingDoor.transform.position + flyingDoor.transform.right * 2f;
        Vector3 doorBackward = flyingDoor.transform.position - flyingDoor.transform.right * 2f;
        Vector3 direction;

        if (Vector3.Distance(doorForward, transform.position) < Vector3.Distance(doorBackward, transform.position))
        {
            // Wendigo is at front of door
            direction = (doorBackward - doorForward).normalized;
            flyingDoor.transform.position = flyingDoor.transform.position - flyingDoor.transform.right;
        }
        else
        {
            // Wendigo is at back of door
            direction = (doorForward - doorBackward).normalized;
            flyingDoor.transform.position = flyingDoor.transform.position + flyingDoor.transform.right;
        }

        Vector3 upDirection = transform.TransformDirection(Vector3.up).normalized * 0.1f;
        Vector3 playerHitDirection = (direction + upDirection).normalized;
        flyingDoor.GetComponent<PlayerHitDoorCollider>().force = playerHitDirection * doorBashForce;

        // Release the Rigidbody from kinematic state
        rb.isKinematic = false;

        // Add an impulse force to the door
        rb.AddForce(direction * doorBashForce, ForceMode.Impulse);

        AudioSource doorAudio = flyingDoor.GetComponent<AudioSource>();
        doorAudio.PlayOneShot(SFX.DoorBash.BashSFX.FromRandom(AI.enemyRandom), 1f);

        string flowType = RoundManager.Instance.dungeonGenerator.Generator.DungeonFlow.name;
        if (flowType == "Level1Flow" || flowType == "Level1FlowExtraLarge" || flowType == "Level1Flow3Exits" || flowType == "Level3Flow")
        {
            doorAudio.PlayOneShot(SFX.DoorBash.MetalDoorSmashSFX.FromRandom(AI.enemyRandom), 0.8f);
        }

        doorAudio.PlayOneShot(SFX.DoorBash.DoorWooshSFX.FromRandom(AI.enemyRandom), 1f);

        triggering = false;
        doorLock = null;
        AI.inSpecialAnimation = false;

        bool despawnDoorAfterBash = true;
        float despawnDoorAfterBashTime = 5f;

        if (despawnDoorAfterBash)
        {
            Destroy(flyingDoor, despawnDoorAfterBashTime);
        }
    }
}
