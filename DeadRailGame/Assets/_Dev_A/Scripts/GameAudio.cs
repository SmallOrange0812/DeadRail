using UnityEngine;

internal static class GameAudio
{
    internal static AudioSource Attach(GameObject owner, string clipName, float volume, float near, float far, bool loop = false)
    {
        AudioSource source = owner.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = Resources.Load<AudioClip>("DeadRailAudio/" + clipName);
        source.loop = loop;
        source.volume = volume;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = near;
        source.maxDistance = far;
        return source;
    }

    internal static void SetLoop(AudioSource source, bool playing)
    {
        if (playing == source.isPlaying) return;
        if (playing) source.Play();
        else source.Stop();
    }
}
