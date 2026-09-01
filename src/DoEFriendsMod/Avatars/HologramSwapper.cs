using System;
using System.Collections.Generic;
using DoEFriendsMod.Gate;
using DoEFriendsMod.Recon;
using UnityEngine;
using Il2Cpp;
using Interop = DoEFriendsMod.Recon.Interop;

namespace DoEFriendsMod.Avatars
{
    /// <summary>
    /// Puts custom avatars on the mannequins in the equipment room.
    ///
    /// Each player slot in the home world shows that player's character on a pedestal, as an
    /// `AvatarHologram : Idler` — a real humanoid rig with its own idle animation, not a static
    /// prop. That makes this cheap: <see cref="PoseRetargeter"/> already copies a pose from any
    /// humanoid Animator, so pointing it at the hologram's animator gives the custom avatar the
    /// mannequin's idling and blinking without writing anything new.
    ///
    /// Cosmetic and local, like everything else here. The hologram is not networked — the
    /// research flagged it as a safe surface for exactly this reason.
    /// </summary>
    public class HologramSwapper
    {
        private class Entry
        {
            public AvatarHologram Hologram;
            public SkinnedMeshRenderer VanillaMesh;
            public bool VanillaMeshWasEnabled;
            public GameObject Model;
            public AvatarBundle Bundle;
            public PoseRetargeter Retarget;
            public string AvatarName;
        }

        private readonly AvatarLibrary _library;
        private readonly AvatarSwapManager _swaps;
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private float _nextScanAt;

        public HologramSwapper(AvatarLibrary library, AvatarSwapManager swaps)
        {
            _library = library;
            _swaps = swaps;
            ModGate.ActiveChanged += active => { if (!active) RevertAll("gate closed"); };
        }

        public int Count => _entries.Count;

        public void Tick(float unscaledTime)
        {
            // Applying the pose has to happen every frame; hunting for holograms does not.
            try { foreach (var kv in _entries) ApplyEntry(kv.Value); }
            catch (Exception e) { Core.Log.Warning($"Hologram apply failed: {e.GetType().Name}: {e.Message}"); }

            if (unscaledTime < _nextScanAt) return;
            _nextScanAt = unscaledTime + 2f;

            if (!ModConfig.HologramSwapEnabled.Value) { RevertAll("disabled in settings"); return; }
            if (!ModGate.Active) return;

            try { Scan(); }
            catch (Exception e) { Core.Log.Warning($"Hologram scan failed: {e.GetType().Name}: {e.Message}"); }
        }

        private void Scan()
        {
            var holograms = UnityEngine.Object.FindObjectsOfType<AvatarHologram>();
            var seen = new HashSet<int>();

            if (holograms != null)
            {
                for (var i = 0; i < holograms.Length; i++)
                {
                    var hologram = holograms[i];
                    if (!Interop.Alive(hologram)) continue;

                    var id = hologram.GetInstanceID();
                    seen.Add(id);

                    var wanted = WantedAvatarFor(hologram);

                    if (_entries.TryGetValue(id, out var existing))
                    {
                        // Someone changed avatar, or took theirs off — rebuild rather than
                        // leaving a mannequin wearing a model nobody is using.
                        if (existing.AvatarName == wanted) continue;
                        Revert(id, "avatar changed");
                        if (wanted == null) continue;
                    }
                    else if (wanted == null) continue;

                    Apply(hologram, wanted);
                }
            }

            List<int> gone = null;
            foreach (var kv in _entries)
                if (!seen.Contains(kv.Key) || !Interop.Alive(kv.Value.Hologram))
                    (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null) foreach (var id in gone) Revert(id, "hologram went away");
        }

        /// <summary>The avatar this mannequin's owner is currently wearing, or null.</summary>
        private string WantedAvatarFor(AvatarHologram hologram)
        {
            try
            {
                var owner = hologram.Owner;
                if (!Interop.Alive(owner)) return null;
                return _swaps.AvatarNameFor(owner.ActorNumber);
            }
            catch { return null; }
        }

        private void Apply(AvatarHologram hologram, string avatarName)
        {
            var manifest = _library.Get(avatarName);
            if (manifest == null) return;

            var bundle = AvatarBundle.Acquire(manifest, out var error);
            if (bundle == null) { Core.Log.Warning($"Hologram: could not load `{avatarName}`: {error}"); return; }

            var entry = new Entry { Hologram = hologram, Bundle = bundle, AvatarName = avatarName };

            try
            {
                Animator source = null;
                try { source = hologram.GetComponent<Animator>(); } catch { }
                if (!Interop.Alive(source)) source = hologram.GetComponentInChildren<Animator>(true);
                if (!Interop.Alive(source))
                {
                    Core.Log.Warning("Hologram: no Animator on the mannequin — skipping.");
                    bundle.Release();
                    return;
                }

                entry.Model = UnityEngine.Object.Instantiate(bundle.Prefab);
                entry.Model.name = $"DFM_Hologram_{avatarName}";
                entry.Model.SetActive(false);

                // Parent to the mannequin so it inherits the pedestal's placement and scale;
                // then sit exactly where the source rig sits.
                entry.Model.transform.SetParent(source.transform, false);
                entry.Model.transform.localPosition = Vector3.zero;
                entry.Model.transform.localRotation = Quaternion.identity;
                entry.Model.transform.localScale = Vector3.one * manifest.rig.suggestedScale;

                foreach (var smr in entry.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (Interop.Alive(smr)) smr.updateWhenOffscreen = true;

                entry.Retarget = new PoseRetargeter();
                var result = entry.Retarget.Build(source, entry.Model, manifest);
                if (entry.Retarget.LinkCount == 0)
                {
                    Core.Log.Warning($"Hologram: {result} — skipping.");
                    UnityEngine.Object.Destroy(entry.Model);
                    bundle.Release();
                    return;
                }

                entry.Model.SetActive(true);

                entry.VanillaMesh = hologram.avatarMesh;
                if (Interop.Alive(entry.VanillaMesh))
                {
                    entry.VanillaMeshWasEnabled = entry.VanillaMesh.enabled;
                    entry.VanillaMesh.enabled = false;
                }

                _entries[hologram.GetInstanceID()] = entry;
                Core.Log.Msg($"Mannequin now showing `{avatarName}` — {result}");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Hologram swap failed: {e.GetType().Name}: {e.Message}");
                if (Interop.Alive(entry.Model)) UnityEngine.Object.Destroy(entry.Model);
                bundle.Release();
            }
        }

        private static void ApplyEntry(Entry entry)
        {
            try { entry.Retarget?.Apply(); } catch { }

            // The hologram rebuilds its mesh whenever cosmetics change (RecreateAvatarMesh),
            // which re-enables the renderer behind our back — so re-assert it every frame.
            // The read has to be inside the try as well as the write: a destroyed renderer
            // throws on `.enabled` just as readily as on assignment, and that threw once per
            // frame for the whole end-of-mission screen.
            try
            {
                if (Interop.Alive(entry.VanillaMesh) && entry.VanillaMesh.enabled)
                    entry.VanillaMesh.enabled = false;
            }
            catch { }
        }

        private void Revert(int id, string why)
        {
            if (!_entries.TryGetValue(id, out var entry)) return;
            _entries.Remove(id);

            if (Interop.Alive(entry.VanillaMesh))
            {
                try { entry.VanillaMesh.enabled = entry.VanillaMeshWasEnabled; } catch { }
            }
            if (Interop.Alive(entry.Model))
            {
                try { UnityEngine.Object.Destroy(entry.Model); } catch { }
            }
            entry.Bundle?.Release();
            Core.Log.Msg($"Mannequin reverted ({why}).");
        }

        public void RevertAll(string why)
        {
            var ids = new List<int>(_entries.Keys);
            foreach (var id in ids) Revert(id, why);
        }
    }
}
