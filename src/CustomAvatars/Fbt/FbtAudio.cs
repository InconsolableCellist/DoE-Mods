using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Fbt
{
    /// <summary>
    /// Audio feedback for FBT state changes, because the person doing the T-pose is wearing a
    /// headset and cannot see the desktop overlay or the console. Each transition has its own
    /// earcon: rising means engaged, falling means off, a low buzz means refused.
    ///
    /// The tones are synthesized into AudioClips at first use — no asset to ship, no game
    /// audio to borrow — and played 2D on a persistent hidden source so they follow the
    /// camera. If any of that throws on this Unity build, the whole thing turns itself off
    /// and FBT continues silently; sound is feedback, not function.
    /// </summary>
    public static class FbtAudio
    {
        private const int SampleRate = 44100;
        private const float Volume = 0.5f;

        private static AudioSource _source;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static bool _dead;

        public static void On() => Play("on", (660f, 0.10f));
        public static void Off() => Play("off", (330f, 0.16f));
        public static void Armed() => Play("armed", (440f, 0.09f), (0f, 0.05f), (440f, 0.09f));
        public static void Locked() => Play("locked", (660f, 0.09f), (0f, 0.03f), (880f, 0.14f));
        public static void Cancelled() => Play("cancelled", (520f, 0.08f), (0f, 0.03f), (390f, 0.10f));
        public static void Error() => Play("error", (220f, 0.28f));

        private static void Play(string name, params (float hz, float seconds)[] pattern)
        {
            if (_dead || !ModConfig.FbtAudioCues.Value) return;
            try
            {
                if (!Interop.Alive(_source))
                {
                    var holder = new GameObject("DFM_FbtAudio");
                    UnityEngine.Object.DontDestroyOnLoad(holder);
                    _source = holder.AddComponent<AudioSource>();
                    _source.playOnAwake = false;
                    _source.spatialBlend = 0f;   // in your ears, not in the room
                }

                if (!Clips.TryGetValue(name, out var clip) || !Interop.Alive(clip))
                    Clips[name] = clip = Build(name, pattern);

                _source.PlayOneShot(clip, Volume);
            }
            catch (Exception e)
            {
                _dead = true;
                Core.Log.Warning($"FBT audio cues unavailable, going silent: {e.Message}");
            }
        }

        private static AudioClip Build(string name, (float hz, float seconds)[] pattern)
        {
            var total = 0f;
            foreach (var (_, seconds) in pattern) total += seconds;
            var sampleCount = Mathf.Max(1, Mathf.CeilToInt(total * SampleRate));
            var data = new Il2CppStructArray<float>(sampleCount);

            var cursor = 0;
            foreach (var (hz, seconds) in pattern)
            {
                var count = Mathf.CeilToInt(seconds * SampleRate);
                // 5 ms fade at each end of every segment, or the edges click.
                var fade = Mathf.Min(count / 2, SampleRate / 200);
                for (var i = 0; i < count && cursor < sampleCount; i++, cursor++)
                {
                    if (hz <= 0f) continue;   // a rest — data is already zero
                    var envelope = 1f;
                    if (i < fade) envelope = i / (float)fade;
                    else if (i > count - fade) envelope = (count - i) / (float)fade;
                    data[cursor] = Mathf.Sin(2f * Mathf.PI * hz * i / SampleRate) * envelope;
                }
            }

            var clip = AudioClip.Create($"DFM_Fbt_{name}", sampleCount, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
