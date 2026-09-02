using System;
using System.Collections.Generic;
using CustomAvatars.Gate;
using CustomAvatars.Recon;
using UnityEngine;
using Il2Cpp;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
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
    /// The mannequin's animator only ever supplies a humanoid pose, so everything that isn't
    /// a humanoid bone — a tail, ears, hair, fingers, a face — stayed frozen in bind pose,
    /// which is why a tail stuck straight out on the pedestal while the same avatar's tail
    /// swung perfectly well in the world. Those parts are driven by the same three components
    /// the in-world avatar uses, built per mannequin: <see cref="SpringBones"/>,
    /// <see cref="Face.FaceDriver"/> and <see cref="HandPoser"/>.
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
            public SpringBones Springs;
            public Face.FaceDriver Face;
            public HandPoser Hands;
            public string AvatarName;
            /// <summary>The avatar's own scale (prefab root times suggestedScale), before the wearer's fit is applied on top.</summary>
            public Vector3 BaseScale = Vector3.one;
            /// <summary>Whose mannequin this is; -1 when nobody owns it, as in the menu.</summary>
            public int ActorNumber = -1;
            /// <summary>Show your face and your fingers on it, rather than a peer's.</summary>
            public bool IsSelf = true;
            public AvatarPlayer Owner;
        }

        private readonly AvatarLibrary _library;
        private readonly AvatarSwapManager _swaps;
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly float[] _curls = new float[10];   // five fingers per hand
        private float _nextScanAt;

        public HologramSwapper(AvatarLibrary library, AvatarSwapManager swaps)
        {
            _library = library;
            _swaps = swaps;
            ModGate.LocalVisualsChanged += allowed => { if (!allowed) RevertAll("local visuals off"); };
        }

        public int Count => _entries.Count;

        public void Tick(float unscaledTime, float deltaTime)
        {
            // Applying the pose has to happen every frame; hunting for holograms does not.
            try { foreach (var kv in _entries) ApplyEntry(kv.Value, deltaTime); }
            catch (Exception e) { Core.Log.Warning($"Hologram apply failed: {e.GetType().Name}: {e.Message}"); }

            if (unscaledTime < _nextScanAt) return;
            _nextScanAt = unscaledTime + 2f;

            if (!ModConfig.HologramSwapEnabled.Value) { RevertAll("disabled in settings"); return; }
            if (!ModGate.LocalVisuals) return;

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
                        if (existing.AvatarName == wanted) { ResolveOwner(existing, hologram); continue; }
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
                if (Interop.Alive(owner)) return _swaps.AvatarNameFor(owner.ActorNumber);

                // No owner. In the menu there is no player object for a mannequin to belong to,
                // and the only person it could possibly be showing is you, so show what you mean
                // to wear. Anywhere else an owner-less mannequin isn't ours to touch.
                return ModGate.Active ? null : _swaps.WantedSelfAvatar;
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

                if (!Interop.Alive(bundle.Prefab))
                {
                    Core.Log.Warning("Hologram: the avatar prefab is no longer loaded — skipping.");
                    bundle.Release();
                    return;
                }

                entry.Model = UnityEngine.Object.Instantiate(bundle.Prefab);
                entry.Model.name = $"DFM_Hologram_{avatarName}";
                entry.Model.SetActive(false);
                Core.Log.Msg($"    mannequin Animator: {AvatarBundle.QuietAnimators(entry.Model)}");

                // Parent to the mannequin so it inherits the pedestal's placement and scale;
                // then sit exactly where the source rig sits.
                entry.Model.transform.SetParent(source.transform, false);
                entry.Model.transform.localPosition = Vector3.zero;
                entry.Model.transform.localRotation = Quaternion.identity;
                entry.BaseScale = AvatarBundle.RootScale(entry.Model, "mannequin") * manifest.rig.suggestedScale;
                entry.Model.transform.localScale = entry.BaseScale;

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
                ResolveOwner(entry, hologram);
                BuildSecondaryMotion(entry, manifest);

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

        /// <summary>
        /// Everything the humanoid pose doesn't cover: the swinging bones, the face and the
        /// fingers. Built after the model is parented, scaled and posed, because the spring
        /// chains capture their rest lengths from live world positions — build them in bind
        /// pose at the origin and every chain starts out stretched toward it.
        /// </summary>
        private static void BuildSecondaryMotion(Entry entry, AvatarManifest manifest)
        {
            try { entry.Retarget.Apply(); } catch { }

            entry.Springs = new SpringBones { ForceScale = MannequinScale(entry.Model, manifest) };
            var springs = entry.Springs.Build(entry.Model, manifest);
            if (entry.Springs.ChainCount == 0) entry.Springs = null;
            else entry.Springs.Reset();

            entry.Face = new Face.FaceDriver();
            var face = entry.Face.Build(entry.Model, manifest);
            if (entry.Face.TargetCount == 0) entry.Face = null;

            entry.Hands = new HandPoser();
            var hands = entry.Hands.Build(entry.Model, manifest);
            if (entry.Hands.JointCount == 0) entry.Hands = null;

            Core.Log.Msg($"    mannequin dynamics: {springs}");
            Core.Log.Msg($"    mannequin face: {face}");
            Core.Log.Msg($"    mannequin hands: {hands}");
        }

        /// <summary>
        /// How big this copy is next to the one that walks around the world, which is the size
        /// the spring constants are tuned against. A pedestal that shrinks its occupant would
        /// otherwise give them a tail swinging through several times the intended angle.
        /// </summary>
        private static float MannequinScale(GameObject model, AvatarManifest manifest)
        {
            try
            {
                var inWorld = Mathf.Max(0.01f, manifest.rig.suggestedScale);
                return Mathf.Clamp(model.transform.lossyScale.y / inWorld, 0.1f, 10f);
            }
            catch { return 1f; }
        }

        /// <summary>
        /// Whose mannequin this is. Re-read on every scan rather than cached at build: a slot
        /// can change hands without the avatar on it changing, and the face on it would then
        /// be the wrong person's.
        /// </summary>
        private static void ResolveOwner(Entry entry, AvatarHologram hologram)
        {
            AvatarPlayer owner = null;
            var actor = -1;
            try
            {
                owner = hologram.Owner;
                if (Interop.Alive(owner)) actor = owner.ActorNumber; else owner = null;
            }
            catch { owner = null; }

            var localActor = -1;
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (Interop.Alive(local)) localActor = local.ActorNumber;
            }
            catch { }

            entry.Owner = owner;
            entry.ActorNumber = actor;
            // An owner-less mannequin only ever gets a model in the menu, where the only person
            // it could be showing is you — the same reasoning as WantedAvatarFor.
            entry.IsSelf = actor < 0 || actor == localActor;
        }

        private void ApplyEntry(Entry entry, float deltaTime)
        {
            PinToPedestal(entry);
            ApplyWearerFit(entry);
            try { entry.Retarget?.Apply(); } catch { }

            // Same order as the in-world avatar: body, then fingers, then face, then the
            // chains that hang off all of it.
            ApplyHands(entry, deltaTime);
            ApplyFace(entry, deltaTime);
            ApplySprings(entry, deltaTime);

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

        /// <summary>
        /// The model sits exactly on the mannequin's rig, and nothing we run should move its
        /// root — the retargeter writes rotations and a hips offset, never the root. Anything
        /// that does move it (a controller the exporter didn't strip, on an older bundle) is
        /// undone here before it can show. Cheap compare, rare write.
        /// </summary>
        private static void PinToPedestal(Entry entry)
        {
            if (!Interop.Alive(entry.Model)) return;
            try
            {
                var t = entry.Model.transform;
                if (t.localPosition.sqrMagnitude > 1e-8f) t.localPosition = Vector3.zero;
                if (Quaternion.Angle(t.localRotation, Quaternion.identity) > 0.01f) t.localRotation = Quaternion.identity;
            }
            catch { }
        }

        /// <summary>
        /// Fingers, taken from whichever body this mannequin is standing in for. Copying the
        /// curls the in-world avatar already computed keeps one reader of the controllers
        /// rather than two, and it is the only source there is for a peer. The menu has no
        /// in-world body to copy from, so there the mannequin reads the controllers itself.
        /// </summary>
        /// <summary>
        /// The mannequin shows the wearer as they are in the world: the avatar at the fit
        /// its wearer has it at, which is how their size (PlayerSize) reaches the pedestal.
        /// The mannequin's own vanilla rig is a fixed 1.5 m body, so this is the one place
        /// the fit has to be applied by hand. Cheap compare, rare write.
        /// </summary>
        private void ApplyWearerFit(Entry entry)
        {
            if (!Interop.Alive(entry.Model)) return;
            try
            {
                var fit = entry.IsSelf ? _swaps.SelfHeightScale : _swaps.RemoteHeightScale(entry.ActorNumber);
                if (!float.IsFinite(fit) || fit <= 0f) fit = 1f;
                var want = entry.BaseScale * fit;
                if ((entry.Model.transform.localScale - want).sqrMagnitude > 1e-8f)
                    entry.Model.transform.localScale = want;
            }
            catch { }
        }

        private void ApplyHands(Entry entry, float deltaTime)
        {
            if (entry.Hands == null) return;
            try
            {
                var source = entry.IsSelf ? _swaps.SelfHandPoser : _swaps.RemoteHandPoser(entry.ActorNumber);
                if (source != null)
                {
                    source.GetCurls(_curls);
                    entry.Hands.RemoteDriven = true;
                    entry.Hands.SetRemoteCurls(_curls);
                }
                else entry.Hands.RemoteDriven = !entry.IsSelf;

                entry.Hands.Update(deltaTime);
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Mannequin hands failed, disabling: {e.Message}");
                entry.Hands = null;
            }
        }

        /// <summary>
        /// The pedestal is the one place you can watch your own face, so it gets the same
        /// treatment the F6 preview does — your tracking, live. A peer's mannequin is driven
        /// from the values their in-world avatar is already being sent, borrowing the same
        /// array rather than keeping a second copy of the stream.
        /// </summary>
        private void ApplyFace(Entry entry, float deltaTime)
        {
            var face = entry.Face;
            if (face == null) return;

            try
            {
                if (entry.IsSelf)
                {
                    var state = Core.Instance?.FaceState;
                    var stale = state?.SecondsSinceLastMessage ?? -1;
                    if (state != null && stale >= 0 && stale < ModConfig.FaceStaleSeconds.Value)
                    {
                        face.Apply(state, deltaTime);
                        return;
                    }
                }
                else
                {
                    var peer = _swaps.RemoteFaceDriver(entry.ActorNumber);
                    var values = peer?.RemoteValues;
                    if (values != null && peer.RemoteAgeSeconds >= 0 &&
                        peer.RemoteAgeSeconds < ModConfig.FaceStaleSeconds.Value)
                    {
                        face.RemoteValues = values;
                        face.ApplyRemote(deltaTime);
                        return;
                    }
                    face.RemoteValues = null;
                }

                // Nobody is sending a face. Fall back to the mouth moving when they speak, and
                // failing that let the expression settle rather than freeze.
                if (ModConfig.VoiceJawEnabled.Value) face.ApplyVoiceJaw(VoiceEnergy(entry), deltaTime);
                else face.Relax(deltaTime);
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Mannequin face failed, disabling: {e.Message}");
                entry.Face = null;
            }
        }

        private static float VoiceEnergy(Entry entry)
        {
            if (!Interop.Alive(entry.Owner)) return 0f;
            try { return entry.Owner.VoiceEnergy; } catch { return 0f; }
        }

        private static void ApplySprings(Entry entry, float deltaTime)
        {
            if (entry.Springs == null || !ModConfig.SpringsEnabled.Value) return;
            try { entry.Springs.Simulate(deltaTime); }
            catch (Exception e)
            {
                Core.Log.Warning($"Mannequin springs failed, disabling: {e.Message}");
                entry.Springs = null;
            }
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
