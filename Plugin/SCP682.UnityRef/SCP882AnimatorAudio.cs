using System.Collections.Generic;
using UnityEngine;

public class SCP682AnimatorAudio : MonoBehaviour
{
    [field: SerializeField] public List<AudioClip> WalkSFX = [];
    [field: SerializeField] public List<AudioClip> RunSFX = [];
    [field: SerializeField] public List<AudioClip> RoarSFX = [];
}
