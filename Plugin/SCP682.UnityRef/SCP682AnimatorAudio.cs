using System.Collections.Generic;
using UnityEngine;
using SCP682.UnityRef.Extensions;

namespace SCP682.UnityRef;

public class SCP682AnimatorAudio : MonoBehaviour
{
    [field: SerializeField] public AudioSource CreatureSFX { get; private set; } = null!;
    [field: SerializeField] public AudioSource CreatureVoice { get; private set; } = null!;

    [field: SerializeField] public List<AudioClip> WalkSFX { get; private set; } = [];
    [field: SerializeField] public List<AudioClip> RunSFX { get; private set; } = [];
    [field: SerializeField] public List<AudioClip> RoarSFX { get; private set; } = [];
    [field: SerializeField] public List<AudioClip> BiteSFX { get; private set; } = [];

    public void PlayWalkSFX() => CreatureSFX.PlayOneShot(WalkSFX.Random());
    public void PlayRunSFX() => CreatureSFX.PlayOneShot(RunSFX.Random());
    public void PlayRoarSFX() => CreatureVoice.PlayOneShot(RoarSFX.Random());
    public void PlayBiteSFX() => CreatureVoice.PlayOneShot(BiteSFX.Random());
}
