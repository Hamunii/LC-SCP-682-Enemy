using System.Collections.Generic;
using UnityEngine;

namespace SCP682.UnityRef.Extensions;


internal static class AudioClipExtensions
{
    private static readonly System.Random random = new();

    public static AudioClip Random(this List<AudioClip> clips)
        => clips[random.Next(clips.Count)];
}
