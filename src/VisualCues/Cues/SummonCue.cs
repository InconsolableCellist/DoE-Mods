using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppPhoton.Pun;
using UnityEngine;
using VisualCues.Hud;
using VisualCues.Net;

namespace VisualCues.Cues
{
    /// <summary>
    /// The "come here" call. Sending: a stick click (or the ' key) broadcasts one event with
    /// the caller's head position, buzzes the hand that clicked, and flashes a confirmation on
    /// the caller's own HUD. Receiving: an arrow to the caller with their name and distance,
    /// following their live avatar for <c>SummonDurationSeconds</c> (refreshed by every new
    /// click), plus the game's own notification line if <c>SummonToast</c> is on.
    ///
    /// Everyone who should see the arrow needs the mod; the caller needs it to send. There is
    /// no roster, so the caller cannot tell who received it — the log counts sends only.
    /// </summary>
    public static class SummonCue
    {
        private sealed class Entry
        {
            public int Actor;
            public string Name;
            public AvatarPlayer Avatar;
            public Vector3 LastPos;
            public float Started, Until;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static float _lastSentAt = -100f;
        public static int Sent { get; private set; }
        public static int Received { get; private set; }

        public static void Init()
        {
            Controls.StickPress.Pressed += stick => Fire(stick);
            CueNet.SummonReceived += OnRemote;
        }

        /// <summary>Send the call. <paramref name="source"/> is "left", "right" or "key".</summary>
        public static void Fire(string source)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.SummonEnabled.Value) return;
            var now = Time.unscaledTime;
            if (now - _lastSentAt < Mathf.Max(0.2f, ModConfig.SummonCooldownSeconds.Value)) return;
            _lastSentAt = now;

            var head = LocalHeadPosition();
            var ok = CueNet.SendSummon(head);
            if (ok) Sent++;
            CueLog.Line(ok ? $"call sent from {source} at {Interop.Vec(head)}" : $"call from {source} NOT sent (not in a room?)");

            if (ModConfig.SummonSentFlash.Value || !ok) CueHud.Flash(ok ? "Call sent" : "Not in a room", ok ? CueHud.SummonColor : Color.gray, 1.2f);
            if (ok && ModConfig.SummonHaptics.Value) Buzz(source);
        }

        private static void Buzz(string source)
        {
            try
            {
                var input = XRInput.Instance;
                if (!Interop.Alive(input)) return;
                var hand = source == "left" ? Handedness.Left : Handedness.Right;
                input.PlayHaptics(hand, 0.7f, 3f);
            }
            catch (Exception e) { if (ModConfig.VerboseLogging.Value) Core.Log.Msg($"Haptics unavailable: {e.GetType().Name}"); }
        }

        private static Vector3 LocalHeadPosition()
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (Interop.Alive(local) && Interop.Alive(local.Head)) return local.Head.position;
            }
            catch { }
            var cam = CueHud.HeadTransform;
            return Interop.Alive(cam) ? cam.position : Vector3.zero;
        }

        private static void OnRemote(int actor, string version, Vector3 pos)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.SummonEnabled.Value) return;
            Received++;
            if (!ModConfig.SummonShowIncoming.Value)
            {
                if (ModConfig.VerboseLogging.Value) CueLog.Line($"call from actor {actor} ignored (SummonShowIncoming off)");
                return;
            }
            var now = Time.unscaledTime;

            AvatarPlayer avatar = null;
            try { avatar = AvatarPlayer.FindByActorNo(actor); } catch { }
            if (!Interop.Alive(avatar)) avatar = null;

            var name = ResolveName(actor, avatar);
            var entry = Entries.Find(e => e.Actor == actor);
            var fresh = entry == null;
            if (fresh) { entry = new Entry { Actor = actor, Started = now }; Entries.Add(entry); }
            entry.Name = name;
            entry.Avatar = avatar;
            if (!float.IsNaN(pos.x)) entry.LastPos = pos;
            else if (avatar != null) entry.LastPos = SafeHead(avatar, entry.LastPos);
            entry.Until = now + Mathf.Max(1f, ModConfig.SummonDurationSeconds.Value);
            if (!fresh) entry.Started = now;   // restart the pulse on every click

            var dist = Vector3.Distance(LocalHeadPosition(), entry.LastPos);
            CueLog.Line($"call from actor {actor} \"{name}\" (mod {version}), {dist:0.0} m away, avatar {(avatar != null ? "found" : "not found")}");
            if (version != Core.Version) Core.Log.Msg($"Call from {name} sent by VisualCues {version}; this is {Core.Version}. The call still works.");

            if (ModConfig.SummonToast.Value)
            {
                try { FXNotifications.AddQuickNotification($"{name} is calling you", 0f); }
                catch (Exception e) { if (ModConfig.VerboseLogging.Value) Core.Log.Msg($"Game notification unavailable: {e.GetType().Name}"); }
            }
            if (ModConfig.SummonUseGameArrow.Value && avatar != null)
            {
                try
                {
                    var target = Interop.Alive(avatar.Head) ? avatar.Head : avatar.transform;
                    FXNotifications.ShowArrow(target, Vector3.up * 0.35f, ModConfig.SummonDurationSeconds.Value, FXNotifications.ArrowIconType.Revive);
                }
                catch (Exception e) { if (ModConfig.VerboseLogging.Value) Core.Log.Msg($"Game arrow unavailable: {e.GetType().Name}"); }
            }
        }

        private static string ResolveName(int actor, AvatarPlayer avatar)
        {
            try
            {
                var room = PhotonNetwork.CurrentRoom;
                var player = room?.GetPlayer(actor);
                if (player != null && !string.IsNullOrWhiteSpace(player.NickName)) return player.NickName;
            }
            catch { }
            try
            {
                if (avatar != null && Interop.Alive(avatar.PVO) && avatar.PVO.Owner != null && !string.IsNullOrWhiteSpace(avatar.PVO.Owner.NickName))
                    return avatar.PVO.Owner.NickName;
            }
            catch { }
            return $"Player {actor}";
        }

        private static Vector3 SafeHead(AvatarPlayer avatar, Vector3 fallback)
        {
            try
            {
                if (Interop.Alive(avatar.Head)) return avatar.Head.position;
                if (Interop.Alive(avatar.transform)) return avatar.transform.position + Vector3.up * 1.5f;
            }
            catch { }
            return fallback;
        }

        /// <summary>Expire entries, follow live avatars, hand the survivors to the HUD.</summary>
        public static void Tick()
        {
            var now = Time.unscaledTime;
            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                var e = Entries[i];
                if (now >= e.Until || !ModConfig.SummonEnabled.Value || !ModConfig.Enabled.Value) { Entries.RemoveAt(i); continue; }
                if (e.Avatar == null || !Interop.Alive(e.Avatar))
                {
                    // The avatar can appear after the call (respawn, late scene load): keep looking.
                    e.Avatar = null;
                    try { var a = AvatarPlayer.FindByActorNo(e.Actor); if (Interop.Alive(a)) e.Avatar = a; } catch { }
                }
                if (e.Avatar != null) e.LastPos = SafeHead(e.Avatar, e.LastPos);
                if (ReachedCaller(e.LastPos))
                {
                    CueLog.Line($"call from \"{e.Name}\" dismissed: close and facing them");
                    Entries.RemoveAt(i);
                    continue;
                }
                CueHud.Submit(new CueHud.Cue
                {
                    Key = $"summon:{e.Actor}",
                    World = e.LastPos,
                    Label = e.Name,
                    Color = CueHud.SummonColor,
                    Started = e.Started,
                    Until = e.Until,
                    Priority = 100,
                    Subtle = true,
                });
            }
        }

        /// <summary>Within SummonDismissMeters of the caller and facing them: the marker has nothing left to say.</summary>
        private static bool ReachedCaller(Vector3 callerHead)
        {
            var limit = ModConfig.SummonDismissMeters.Value;
            if (limit <= 0f) return false;
            var head = CueHud.HeadTransform;
            if (!Interop.Alive(head)) return false;
            var dir = callerHead - head.position;
            if (dir.magnitude > limit) return false;
            dir.y = 0f;
            var fwd = head.forward; fwd.y = 0f;
            if (dir.sqrMagnitude < 0.01f || fwd.sqrMagnitude < 0.01f) return true;
            return Vector3.Angle(fwd, dir) <= ModConfig.SummonFacingAngle.Value;
        }

        public static void Clear(string why)
        {
            if (Entries.Count > 0 && ModConfig.VerboseLogging.Value) Core.Log.Msg($"Dropping {Entries.Count} call marker(s): {why}");
            Entries.Clear();
        }

        public static string Describe() => $"calls sent {Sent}, received {Received}, showing {Entries.Count}";
    }
}
