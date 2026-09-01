using System;
using System.Collections.Generic;
using UnityEngine;

// Il2CppInterop moves the game's global-namespace types (AvatarPlayer, CharacterPrefab, …)
// into an "Il2Cpp" namespace. If this using ever fails to resolve after a MelonLoader
// upgrade, check MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll in a decompiler — the
// generator's namespace rewrite is the only thing that would have changed.
using Il2Cpp;

namespace CustomAvatars.Recon
{
    /// <summary>
    /// Watches AvatarPlayer instances appear and disappear and dumps each one. Polls the
    /// game's own static roster rather than patching spawn methods: it costs nothing, it
    /// can't destabilise the game, and it catches avatars that existed before the melon
    /// woke up.
    ///
    /// Each avatar is dumped twice — once on sight, once after RedumpDelaySeconds. The
    /// merged character mesh is built a beat after the AvatarPlayer exists, and hotkeys
    /// don't reach the game window while you're wearing the headset, so the delayed pass is
    /// what actually captures the finished rig.
    /// </summary>
    public class AvatarWatcher
    {
        private readonly Dictionary<int, string> _known = new Dictionary<int, string>();
        private readonly HashSet<int> _dumpedInitial = new HashSet<int>();
        private readonly Dictionary<int, float> _redumpAt = new Dictionary<int, float>();
        private bool _pooledDumped;
        private float _cooldown;

        /// <summary>Called on scene change: instance IDs don't survive a scene, dumps do.</summary>
        public void Reset()
        {
            _known.Clear();
            _redumpAt.Clear();
        }

        public void Tick()
        {
            RunDueRedumps();

            _cooldown -= Time.unscaledDeltaTime;
            if (_cooldown > 0f) return;
            _cooldown = Mathf.Max(0.25f, ModConfig.AvatarPollSeconds.Value);

            try { Poll(); }
            catch (Exception e) { ReconLog.Error("AvatarWatcher poll", e); }
        }

        private void Poll()
        {
            var all = AvatarPlayer.AllPlayers;
            if (ReferenceEquals(all, null)) return;

            var seen = new HashSet<int>();

            for (var i = 0; i < all.Count; i++)
            {
                var ap = all[i];
                if (!Interop.Alive(ap)) continue;

                var id = ViewIdOf(ap);
                seen.Add(id);

                if (_known.ContainsKey(id)) continue;

                var label = LabelOf(ap, id);
                _known[id] = label;
                ReconLog.Headline($"AvatarPlayer appeared: {label}");

                if (_dumpedInitial.Add(id))
                {
                    Dump(ap, id, "first sight");
                    _redumpAt[id] = Time.unscaledTime + Mathf.Max(0f, ModConfig.RedumpDelaySeconds.Value);
                }
            }

            // Report departures so the transcript shows who was present when. Compare
            // membership, not counts — one player leaving as another joins keeps the count
            // identical while both facts still need logging.
            List<int> gone = null;
            foreach (var kv in _known)
            {
                if (seen.Contains(kv.Key)) continue;
                (gone ??= new List<int>()).Add(kv.Key);
            }
            if (gone != null)
            {
                foreach (var id in gone)
                {
                    ReconLog.Headline($"AvatarPlayer left: {_known[id]}");
                    _known.Remove(id);
                    _redumpAt.Remove(id);
                }
            }

            // The prefab pool only populates once we're actually in a room.
            if (!_pooledDumped && _known.Count > 0)
            {
                _pooledDumped = true;
                try { ModelRecon.DumpNetworkObjectPool(); }
                catch (Exception e) { ReconLog.Error("NetworkObjectPool dump", e); }
            }
        }

        private void RunDueRedumps()
        {
            if (_redumpAt.Count == 0) return;

            List<int> due = null;
            var now = Time.unscaledTime;
            foreach (var kv in _redumpAt)
                if (now >= kv.Value) (due ??= new List<int>()).Add(kv.Key);
            if (due == null) return;

            foreach (var id in due) _redumpAt.Remove(id);

            try
            {
                var all = AvatarPlayer.AllPlayers;
                if (ReferenceEquals(all, null)) return;
                for (var i = 0; i < all.Count; i++)
                {
                    var ap = all[i];
                    if (!Interop.Alive(ap)) continue;
                    if (due.Contains(ViewIdOf(ap))) Dump(ap, ViewIdOf(ap), "settled re-dump");
                }
            }
            catch (Exception e) { ReconLog.Error("delayed re-dump", e); }
        }

        /// <summary>F8: re-dump every AvatarPlayer in the scene, ignoring the once-only rule.</summary>
        public void DumpEverythingNow()
        {
            ReconLog.Section("Manual avatar sweep (F8)");
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType<AvatarPlayer>();
                ReconLog.Headline($"FindObjectsOfType<AvatarPlayer> → {(found == null ? 0 : found.Length)}");
                if (found == null) return;
                for (var i = 0; i < found.Length; i++)
                    if (Interop.Alive(found[i]))
                        Dump(found[i], ViewIdOf(found[i]), "manual F8");
                ModelRecon.DumpNetworkObjectPool();
            }
            catch (Exception e) { ReconLog.Error("manual avatar sweep", e); }
        }

        private static int ViewIdOf(AvatarPlayer ap)
        {
            try
            {
                var pv = ap.PVO;
                if (Interop.Alive(pv)) return pv.ViewID;
            }
            catch { }
            try { return ap.GetInstanceID(); } catch { return 0; }
        }

        private static string LabelOf(AvatarPlayer ap, int id)
        {
            var name = "?";
            var mine = "?";
            try { name = ap.PlayerName; } catch { }
            try { mine = IsLocal(ap) ? "LOCAL" : "remote"; } catch { }
            return $"{name} [{mine}] viewID {id}";
        }

        private static bool IsLocal(AvatarPlayer ap)
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (Interop.Alive(local)) return local.Pointer == ap.Pointer;
            }
            catch { }
            try { return Interop.Alive(ap.PVO) && ap.PVO.IsMine; } catch { return false; }
        }

        private void Dump(AvatarPlayer ap, int id, string pass)
        {
            var local = IsLocal(ap);
            ReconLog.Section($"AvatarPlayer dump ({pass}) — {LabelOf(ap, id)}");

            ReconLog.Line("### Identity & state");
            ReconLog.TryKeyValue("Is local avatar", () => local);
            ReconLog.TryKeyValue("PlayerName", () => ap.PlayerName);
            ReconLog.TryKeyValue("rigType", () => ap.rigType);
            ReconLog.TryKeyValue("HasSpawned", () => ap.HasSpawned);
            ReconLog.TryKeyValue("IsSpectator", () => ap.IsSpectator);
            ReconLog.TryKeyValue("IsAlive", () => ap.IsAlive);
            ReconLog.TryKeyValue("headYHeight", () => ap.headYHeight);
            ReconLog.TryKeyValue("torsoBottomYHeight", () => ap.torsoBottomYHeight);
            ReconLog.TryKeyValue("PhotonView.IsMine", () => ap.PVO.IsMine);
            ReconLog.TryKeyValue("PhotonView.Owner", () => Interop.Alive(ap.PVO) && !ReferenceEquals(ap.PVO.Owner, null)
                ? ap.PVO.Owner.NickName : "<none>");
            ReconLog.Try("CosmeticModules", () =>
            {
                var mods = ap.CosmeticModules;
                if (ReferenceEquals(mods, null)) { ReconLog.KeyValue("CosmeticModules", "null"); return; }
                ReconLog.KeyValue("CosmeticModules", $"{mods.Count} slots");
                for (var i = 0; i < mods.Count; i++)
                {
                    // Item1 (the PlayerData.CharacterValues enum half) reads back as a
                    // constant garbage int through Il2CppInterop's generic ValueTuple, so
                    // print the tuple's own ToString() alongside it — that goes through
                    // il2cpp and gets the slot key right.
                    var entry = mods[i];
                    ReconLog.Line($"  - {SafeTupleString(entry)} | Item1(raw)={entry.Item1} Item2=`{entry.Item2}`");
                }
            });

            ReconLog.Line();
            ReconLog.Line("### The AvatarPlayer object itself");
            ModelRecon.DumpAnchoring(SafeGet(() => ap.transform));

            ReconLog.Line();
            ReconLog.Line("### IK targets (the anchors our parallel rig will follow)");
            DumpTransform("IKTargetHead", SafeGet(() => ap.IKTargetHead));
            DumpTransform("IKTargetLeftHand", SafeGet(() => ap.IKTargetLeftHand));
            DumpTransform("IKTargetRightHand", SafeGet(() => ap.IKTargetRightHand));
            DumpTransform("Head", SafeGet(() => ap.Head));
            DumpTransform("Eye", SafeGet(() => ap.Eye));
            DumpTransform("LeftHand", SafeGet(() => ap.LeftHand));
            DumpTransform("RightHand", SafeGet(() => ap.RightHand));
            DumpTransform("RemoteRig", SafeGet(() => ap.RemoteRig));

            ReconLog.Line();
            ReconLog.Line("### Rig components");
            ReconLog.TryKeyValue("RemoteAnimator", () =>
            {
                var anim = ap.RemoteAnimator;
                if (!Interop.Alive(anim)) return "<null>";
                return $"{Interop.ScenePath(anim.transform)} (isHuman: {SafeIsHuman(anim)}, " +
                       $"avatar: {(Interop.Alive(anim.avatar) ? Interop.Name(anim.avatar) : "<null>")})";
            });

            ReconLog.Line();
            ReconLog.Line("### AvatarPlayer subtree (IK targets and holsters only — the model lives elsewhere)");
            HierarchyDump.Tree(SafeGet(() => ap.transform));

            // The visual model is a separate scene-root object; this is where the merged
            // mesh, the bones, the character shader and FinalIK actually live.
            ReconLog.Try("FullBody model", () => ModelRecon.DumpCharacterPrefab("FullBody model", ap.FullBody));

            // For the local VR player the hands come from the SteamVR rig, not the avatar —
            // dump that rig too, so "does my own body render" has a complete answer.
            if (local)
            {
                ReconLog.Try("local VR rig", () =>
                {
                    var root = ModelRecon.RootOf(ap.Head);
                    if (Interop.Alive(root))
                        ModelRecon.DumpSubtree("Local VR rig (source of the self hands)", root, 8);
                });
            }

            ReconLog.Headline($"Avatar dump ({pass}) complete → {ReconLog.CurrentFile}");
        }

        private static string SafeTupleString(Il2CppSystem.ValueTuple<PlayerData.CharacterValues, string> t)
        {
            try { return t.ToString(); } catch (Exception e) { return $"<{e.GetType().Name}>"; }
        }

        private static string SafeIsHuman(Animator a)
        {
            try { return a.isHuman.ToString(); } catch { return "?"; }
        }

        private static Transform SafeGet(Func<Transform> get)
        {
            try { return get(); } catch { return null; }
        }

        private static void DumpTransform(string label, Transform t)
        {
            if (!Interop.Alive(t)) { ReconLog.KeyValue(label, "<null>"); return; }
            try
            {
                ReconLog.KeyValue(label, $"`{Interop.ScenePath(t)}` world {Interop.Vec(t.position)}");
            }
            catch (Exception e) { ReconLog.Error(label, e); }
        }
    }
}
