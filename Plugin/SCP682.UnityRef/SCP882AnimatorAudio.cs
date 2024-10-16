using System.Collections.Generic;
using UnityEngine;
using SCP682.UnityRef.Extensions;

namespace SCP682.UnityRef;

public class SCP682AnimatorAudio : MonoBehaviour
{
    [field: SerializeField] public AudioSource CreatureSFX { get; } = null!;
    [field: SerializeField] public AudioSource CreatureVoice { get; } = null!;

    [field: SerializeField] public List<AudioClip> WalkSFX { get; } = [];
    [field: SerializeField] public List<AudioClip> RunSFX { get; } = [];
    [field: SerializeField] public List<AudioClip> RoarSFX { get; } = [];
    [field: SerializeField] public List<AudioClip> BiteSFX { get; } = [];

    public void PlayWalkSFX() => CreatureSFX.PlayOneShot(WalkSFX.Random());
    public void PlayRunSFX() => CreatureSFX.PlayOneShot(RunSFX.Random());
    public void PlayRoarSFX() => CreatureVoice.PlayOneShot(RoarSFX.Random());
    public void PlayBiteSFX() => CreatureVoice.PlayOneShot(BiteSFX.Random());
}
