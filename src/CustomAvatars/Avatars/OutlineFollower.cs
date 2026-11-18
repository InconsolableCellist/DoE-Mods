using System;
using System.Collections.Generic;
using CustomAvatars.Recon;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Makes the game's blue outline trace the custom avatar instead of the hidden vanilla body
    /// (0.42.23).
    ///
    /// How the game draws it (dump of 2026-11-13, see GAME-INTERNALS.md → Enemies → Outlines):
    /// each player's <c>CharacterPrefab.highlightEffect</c> is an <c>FXOutline</c> that keeps its
    /// own renderer list, <c>Entries</c>, filled by <c>FXOutline.ResolveRenderers</c> from its own
    /// hierarchy. <c>FXOutlineManager</c> then draws those renderers from a command buffer
    /// (<c>CommandBuffer.DrawRenderer</c>), which ignores a renderer's <c>shadowCastingMode</c>,
    /// so the body we hide with ShadowsOnly is still outlined. The avatar is a scene root, never
    /// under the <c>CharacterPrefab</c>, so the game can never find it on its own.
    ///
    /// <c>CharacterPrefab.EnableXRayMode</c> returns early for the local player: you never see
    /// your own outline. What this fixes is the outline your client draws of a friend (stick
    /// press, a teammate in another room), and their client does the same for you.
    ///
    /// The fix: a postfix on <c>FXOutline.ResolveRenderers</c>, the one call every refresh goes
    /// through (it is called from <c>OnEnable</c>, <c>Refresh</c> and <c>UpdateRegistration</c>).
    /// For an outline that belongs to a swapped player it replaces the entries with the avatar's
    /// renderers. The HD shell source (<c>shellSource</c>, a skinned mesh the HD mode welds a shell
    /// from) is cleared while swapped and put back on revert, so nothing still points at the
    /// vanilla body. Everything is keyed on the outline object, so the same call can stand an
    /// enemy skin in for an enemy later (CUSTOM-ENEMIES-PLAN, Phase 1).
    /// </summary>
    public static class OutlineFollower
    {
        private sealed class Stand
        {
            public FXOutline Outline;
            public GameObject Model;
            public SkinnedMeshRenderer ShellWas;
            public Mesh BakedWas;
            public string Who;
            public int Rewrites;
        }

        private static readonly Dictionary<IntPtr, Stand> Stands = new Dictionary<IntPtr, Stand>();
        private static bool _installed, _rewriting, _loggedFail;

        public static bool Installed => _installed;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                // ResolveRenderers has its own body (RVA 0x769020 in the 1.3 build, once in dump.cs).
                var target = AccessTools.Method(typeof(FXOutline), "ResolveRenderers");
                if (target == null) { Core.Log.Warning("Outline: FXOutline.ResolveRenderers not found — outlines keep tracing the vanilla body."); return; }
                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(OutlineFollower), nameof(Resolve_Postfix))));
                _installed = true;
                Core.Log.Msg("Outline: FXOutline.ResolveRenderers hooked — a swapped player's outline traces the avatar.");
            }
            catch (Exception e) { Core.Log.Warning($"Outline: could not hook ResolveRenderers ({e.GetType().Name}: {e.Message})."); }
        }

        /// <summary>Stand <paramref name="model"/>'s renderers in for <paramref name="body"/>'s in its outline.</summary>
        public static void Attach(CharacterPrefab body, GameObject model, string who)
        {
            if (!_installed || !ModConfig.OutlineFollowsAvatar.Value) return;
            try
            {
                if (!Interop.Alive(body) || !Interop.Alive(model)) return;
                var fx = body.highlightEffect;
                if (!Interop.Alive(fx)) { Core.Log.Msg($"    outline: {who} has no highlight effect; nothing to redirect"); return; }
                Attach(fx, model, who);
            }
            catch (Exception e) { Core.Log.Warning($"    outline: attach failed for {who}: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>The general form: any outline, any stand-in model (an enemy skin, later).</summary>
        public static void Attach(FXOutline fx, GameObject model, string who)
        {
            if (!_installed || !Interop.Alive(fx) || !Interop.Alive(model)) return;
            if (!Stands.TryGetValue(fx.Pointer, out var s))
            {
                s = new Stand { Outline = fx, Who = who };
                try { s.ShellWas = fx.shellSource; } catch { }
                try { s.BakedWas = fx.bakedShellMesh; } catch { }
                Stands[fx.Pointer] = s;
            }
            s.Model = model;
            Rewrite(s, "attach");
        }

        /// <summary>Give the outline back to the game's own renderers.</summary>
        public static void Detach(CharacterPrefab body)
        {
            try
            {
                if (!Interop.Alive(body)) return;
                var fx = body.highlightEffect;
                if (Interop.Alive(fx)) Detach(fx);
            }
            catch { }
        }

        public static void Detach(FXOutline fx)
        {
            if (!Interop.Alive(fx) || !Stands.TryGetValue(fx.Pointer, out var s)) return;
            Stands.Remove(fx.Pointer);
            try
            {
                fx.ReleaseWeldData();
                try { if (FXOutlineManager.hasInstance) FXOutlineManager.Instance.ReleaseSkinnedBinding(fx); } catch { }
                fx.shellSource = s.ShellWas;
                fx.bakedShellMesh = s.BakedWas;
                _rewriting = true;
                try { fx.ResolveRenderers(); } finally { _rewriting = false; }
                fx.resolveVersion++;
                fx.MarkDirty();
                Core.Log.Msg($"    outline: {s.Who}'s outline is back on the game's body");
            }
            catch (Exception e) { Core.Log.Warning($"    outline: detach failed for {s.Who}: {e.GetType().Name}: {e.Message}"); }
        }

        private static void Resolve_Postfix(FXOutline __instance)
        {
            // Runs inside the game's render bookkeeping. Never throw out of here.
            try
            {
                if (_rewriting || Stands.Count == 0 || ReferenceEquals(__instance, null)) return;
                if (!Stands.TryGetValue(__instance.Pointer, out var s)) return;
                if (!Interop.Alive(s.Model)) { Stands.Remove(__instance.Pointer); return; }
                Rewrite(s, "resolve");
            }
            catch (Exception e)
            {
                if (_loggedFail) return;
                _loggedFail = true;
                Core.Log.Warning($"Outline: rewrite failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Rewrite(Stand s, string why)
        {
            var fx = s.Outline;
            if (!Interop.Alive(fx) || !Interop.Alive(s.Model)) return;
            var entries = fx.Entries;
            if (entries == null) return;
            var added = 0;
            entries.Clear();
            foreach (var r in s.Model.GetComponentsInChildren<Renderer>(true))
            {
                if (!Interop.Alive(r)) continue;
                Mesh mesh = null;
                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (smr != null) mesh = smr.sharedMesh;
                else if (r.TryCast<MeshRenderer>() != null)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (Interop.Alive(mf)) mesh = mf.sharedMesh;
                }
                if (!Interop.Alive(mesh)) continue;   // particles, trails, lines: not outlined
                entries.Add(new FXOutline.Entry { renderer = r, mesh = mesh, isSkinned = smr != null, subMeshCount = mesh.subMeshCount });
                added++;
            }
            // HD mode welds a shell from this skinned mesh; it is the vanilla body, so it goes while
            // swapped. Its weld buffers and the manager's binding are released once, on attach, not
            // on every refresh (ResolveRenderers runs on each OnEnable, i.e. every x-ray toggle).
            if (why == "attach")
            {
                fx.ReleaseWeldData();
                try { if (FXOutlineManager.hasInstance) FXOutlineManager.Instance.ReleaseSkinnedBinding(fx); } catch { }
            }
            fx.shellSource = null;
            fx.bakedShellMesh = null;
            fx.resolveVersion++;
            fx.MarkDirty();
            s.Rewrites++;
            if (why == "attach" || s.Rewrites <= 3)
                Core.Log.Msg($"    outline: {s.Who}'s outline traces the avatar ({added} renderer(s), {why})");
        }

        /// <summary>For the outline probe (Keypad4): what each player's outline holds right now.</summary>
        public static void Dump()
        {
            ReconLog.Section("Outlines (Keypad4)");
            ReconLog.KeyValue("hook installed", _installed);
            ReconLog.KeyValue("OutlineFollowsAvatar", ModConfig.OutlineFollowsAvatar.Value);
            ReconLog.TryKeyValue("FXOutlineManager.HDMode", () => FXOutlineManager.HDMode);
            ReconLog.TryKeyValue("FXOutlineManager.Disabled", () => FXOutlineManager.Disabled);
            ReconLog.TryKeyValue("XRayPing.XRayEnabled", () => XRayPing.XRayEnabled);
            ReconLog.KeyValue("redirected outlines", Stands.Count);
            var players = AvatarPlayer.AllPlayers;
            if (players == null) { ReconLog.Line("- no player list"); return; }
            for (var pi = 0; pi < players.Count; pi++)
            {
                var p = players[pi];
                if (!Interop.Alive(p)) continue;
                ReconLog.Try("player outline", () =>
                {
                    CharacterPrefab cp = null;
                    try { cp = p.FullBody; } catch { }
                    var fx = Interop.Alive(cp) ? cp.highlightEffect : null;
                    ReconLog.Line();
                    ReconLog.Line($"### {SafeNick(p)}{(p.IsLocal ? " (you)" : "")}");
                    if (!Interop.Alive(fx)) { ReconLog.Line("- no highlight effect"); return; }
                    ReconLog.KeyValue("path", Interop.ScenePath(fx.transform));
                    ReconLog.TryKeyValue("enabled / highlighted", () => $"{fx.enabled} / {fx.highlighted}");
                    ReconLog.TryKeyValue("glowVisibility", () => fx.glowVisibility);
                    ReconLog.TryKeyValue("colour, width", () => $"{fx.outlineColor} , {fx.glowWidth}");
                    ReconLog.TryKeyValue("shellSource", () => Interop.Alive(fx.shellSource) ? Interop.ScenePath(fx.shellSource.transform) : "null");
                    ReconLog.TryKeyValue("bakedShellMesh", () => Interop.Alive(fx.bakedShellMesh) ? Interop.Name(fx.bakedShellMesh) : "null");
                    ReconLog.TryKeyValue("effectNameFilter", () => fx.effectNameFilter ?? "");
                    ReconLog.KeyValue("redirected", Stands.ContainsKey(fx.Pointer));
                    var entries = fx.Entries;
                    ReconLog.KeyValue("entries", entries == null ? -1 : entries.Count);
                    if (entries == null) return;
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        var r = e.renderer;
                        ReconLog.Line($"  - [{i}] {(Interop.Alive(r) ? Interop.ScenePath(r.transform) : "<null>")} " +
                                      $"{(e.isSkinned ? "skinned" : "static")}, {e.subMeshCount} submesh(es), " +
                                      $"shadowCastingMode {(Interop.Alive(r) ? r.shadowCastingMode.ToString() : "?")}");
                    }
                });
            }
        }

        private static string SafeNick(AvatarPlayer p)
        {
            try { return p.PlayerName; } catch { return "?"; }
        }

        public static void Clear() => Stands.Clear();
    }
}
