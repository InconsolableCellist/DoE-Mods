using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using AI = Il2CppSauron.AI;
using Sauron = Il2CppSauron.Sauron;
using SauronKeys = Il2CppSauron.SauronKeys;

namespace CustomAvatars.Recon
{
    /// <summary>
    /// The runtime half of custom enemies Phase 0a (docs/CUSTOM-ENEMIES-PLAN.md). Answers what
    /// the dump cannot: Death's rig, modules, animator, motion and kill values, and how a dungeon
    /// is lit. Everything goes to the recon transcript (UserData/CustomAvatars/recon), headlines
    /// to the console. Read-only: nothing here changes an enemy, a renderer or a setting.
    ///
    /// Keys (numpad, unused by every mod here):
    ///   Keypad7  enemies: every live enemy in one line, then Death in full (live, else the
    ///            nearest enemy), then the Death prefabs from Resources.
    ///   Keypad8  start / stop sampling live Death once a second, plus its animation events.
    ///   Keypad9  lighting and post-processing where you stand.
    ///   Keypad4  outlines (see Avatars.OutlineFollower.Dump).
    /// </summary>
    public static class EnemyRecon
    {
        private static readonly string[] Prefabs = { "death", "death_hazard", "death_nest", "death_whistle", "playground_enemies", "ai sandbox" };

        private static bool _sampling;
        private static float _nextSample;
        private static int _deathHash;
        private static bool _hashTried;
        private static readonly Dictionary<IntPtr, string> LastState = new Dictionary<IntPtr, string>();
        private static bool _wasDead;

        public static bool Sampling => _sampling;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            // AE_FX (RVA 0xB16360) and AE_Footstep (RVA 0xB163C0) each have their own body in the 1.3 build.
            TryPatch(harmony, "AE_FX", nameof(AEFX_Postfix));
            TryPatch(harmony, "AE_Footstep", nameof(AEFootstep_Postfix));
        }

        private static void TryPatch(HarmonyLib.Harmony harmony, string method, string postfix)
        {
            try
            {
                var target = AccessTools.Method(typeof(AI), method);
                if (target == null) { Core.Log.Warning($"Enemy recon: AI.{method} not found."); return; }
                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(EnemyRecon), postfix)));
            }
            catch (Exception e) { Core.Log.Warning($"Enemy recon: could not hook AI.{method} ({e.GetType().Name}: {e.Message})."); }
        }

        // ------------------------------------------------------------------ identity
        private static int DeathHash()
        {
            if (!_hashTried)
            {
                _hashTried = true;
                try { _deathHash = SauronKeys.IDHashDeath; } catch (Exception e) { Core.Log.Warning($"Enemy recon: SauronKeys.IDHashDeath unavailable ({e.Message})"); }
            }
            return _deathHash;
        }

        private static bool IsDeath(AI ai)
        {
            try { var r = ai.references; return r != null && DeathHash() != 0 && r.IDHash == DeathHash(); }
            catch { return false; }
        }

        private static List<AI> Live()
        {
            var list = new List<AI>();
            try
            {
                var all = Sauron.ActiveAIList;
                if (all == null) return list;
                for (var i = 0; i < all.Count; i++) if (Interop.Alive(all[i])) list.Add(all[i]);
            }
            catch (Exception e) { ReconLog.Error("Sauron.ActiveAIList", e); }
            return list;
        }

        private static Vector3 HeadPos()
        {
            try { var p = AvatarPlayer.LocalAvatar; if (Interop.Alive(p)) return p.Head.position; } catch { }
            try { return Camera.main.transform.position; } catch { return Vector3.zero; }
        }

        // ------------------------------------------------------------------ Keypad7
        public static void DumpEnemies()
        {
            ReconLog.Section("Enemies (Keypad7)");
            var live = Live();
            var head = HeadPos();
            ReconLog.KeyValue("live enemies", live.Count);
            ReconLog.KeyValue("IDHashDeath", DeathHash());
            AI nearest = null; var best = float.MaxValue;
            var deaths = new List<AI>();
            foreach (var ai in live)
            {
                ReconLog.Try("enemy line", () =>
                {
                    var r = ai.references;
                    var d = Vector3.Distance(ai.transform.position, head);
                    if (d < best) { best = d; nearest = ai; }
                    if (IsDeath(ai)) deaths.Add(ai);
                    ReconLog.Line($"- `{ai.name}` ID {r?.ID} hash {r?.IDHash} type {ai.type} class {r?.spawnedAsClass} " +
                                  $"{d:0.0} m away{(IsDeath(ai) ? "  **DEATH**" : "")}");
                });
            }
            var full = deaths.Count > 0 ? deaths : (nearest != null ? new List<AI> { nearest } : new List<AI>());
            if (deaths.Count == 0) ReconLog.Line(nearest == null ? "_No live enemy. Spawn Death in the sandbox (SPAWN THE DEATH REAPER) and press Keypad7 again._" : "_No live Death; the nearest enemy in full instead._");
            foreach (var ai in full) DumpAI(ai);

            foreach (var name in Prefabs) ReconLog.Try($"prefab {name}", () => DumpPrefab(name));
            ReconLog.Headline($"Enemy recon: {live.Count} live enemy(ies), {deaths.Count} Death; written to {ReconLog.CurrentFile}");
        }

        private static void DumpPrefab(string name)
        {
            var o = Resources.Load(name);
            ReconLog.Line();
            ReconLog.Line($"### Resources `{name}`: {(o == null ? "not found" : HierarchyDump.TypeName(o))}");
            if (o == null) return;
            var go = o.TryCast<GameObject>();
            if (go == null) return;
            ReconLog.Line("Hierarchy (prefab asset):");
            HierarchyDump.Tree(go.transform, 8);
            DumpRenderers(go.transform);
            DumpColliders(go.transform);
            DumpAnimators(go.transform);
            var ai = go.GetComponentInChildren<AI>(true);
            if (Interop.Alive(ai)) DumpModules(ai);
        }

        private static void DumpAI(AI ai)
        {
            ReconLog.Line();
            ReconLog.Line($"### Live `{ai.name}`{(IsDeath(ai) ? " (Death)" : "")}");
            ReconLog.TryKeyValue("path", () => Interop.ScenePath(ai.transform));
            ReconLog.TryKeyValue("position / scale", () => $"{Interop.Vec(ai.transform.position)} / {Interop.Vec(ai.transform.lossyScale)}");
            ReconLog.TryKeyValue("type / armorTier / IsMine", () => $"{ai.type} / {ai.armorTier} / {ai.IsMine}");
            ReconLog.TryKeyValue("rootMotionMode", () => ai.rootMotionMode);
            ReconLog.Try("references", () =>
            {
                var r = ai.references;
                ReconLog.Line($"- **references**: ID {r.ID}, family {r.family}, defaultSpawnType {r.defaultSpawnType}, spawnedAsClass {r.spawnedAsClass}, " +
                              $"isHuman {r.isHuman}, isPuppet {r.isPuppet}, isPhysicsDriven {r.isPhysicsDriven}, despawnMode {r.despawnMode}");
                ReconLog.Line($"- **navigation**: solver {r.navSolverType}, agent {r.navAgentType}, radius {r.navAgentRadius}, height {r.navAgentHeight}, " +
                              $"baseOffset {r.baseOffset}, squeezeFloor {r.squeezeFloor}, constrainToPath {r.constrainToPath}");
                ReconLog.Line($"- **defaultScale** {Interop.Vec(r.defaultScale)}, head `{(Interop.Alive(r.head) ? Interop.ScenePath(r.head) : "null")}`, " +
                              $"defaultStateHash {r.defaultStateHash}");
                ReconLog.Line($"- **materialHandler** {(r.materialHandler == null ? "null" : "set")}, characterOptimizer {(Interop.Alive(r.characterOptimizer) ? Interop.ScenePath(r.characterOptimizer.transform) : "null")}");
            });
            ReconLog.Try("health", () => ReconLog.Line($"- **health**: maxHP {ai.health.maxHP}, HP {ai.health.HP.Value}, mortality {ai.health.mortality}, regain {ai.health.regainSpeed}"));
            ReconLog.Try("specs", () =>
            {
                var s = ai.specs;
                if (s == null) { ReconLog.KeyValue("specs", "null"); return; }
                ReconLog.Line($"- **specs** `{s.name}`: HP {s.HP}, multHPByNumPlayers {s.multHPByNumPlayers}, scale {s.scale}");
            });
            DumpModules(ai);
            ReconLog.Line("Hierarchy (live):");
            HierarchyDump.Tree(ai.transform, 8);
            DumpRenderers(ai.transform);
            DumpColliders(ai.transform);
            DumpAnimators(ai.transform);
            ReconLog.Try("audio", () =>
            {
                var a = ai.GetComponentsInChildren<AudioSource>(true);
                ReconLog.KeyValue("audio sources", a == null ? 0 : a.Length);
            });
            ReconLog.Try("lights", () =>
            {
                foreach (var l in ai.GetComponentsInChildren<Light>(true))
                    ReconLog.Line($"- light `{Interop.ScenePath(l.transform)}` {l.type} range {l.range} intensity {l.intensity} colour {l.color}");
            });
        }

        private static void DumpModules(AI ai)
        {
            ReconLog.Try("module slots", () =>
            {
                var slots = ai.AIModules;
                if (slots == null) return;
                var sb = new StringBuilder();
                foreach (var slot in slots)
                {
                    if (slot == null || !Interop.Alive(slot.module)) continue;
                    var m = slot.module;
                    sb.Append($"{HierarchyDump.TypeName(m)} `{m.name}`");
                    try { if (Interop.Alive(m.animatorController)) sb.Append($" [controller {m.animatorController.name}]"); } catch { }
                    sb.Append("; ");
                    DumpModuleSettings(m);
                }
                ReconLog.KeyValue("module settings", sb.ToString());
            });
            ReconLog.Try("live modules", () =>
            {
                var mods = ai.modules;
                if (mods == null) return;
                var names = new List<string>();
                foreach (var m in mods)
                {
                    if (m == null) continue;
                    names.Add(HierarchyDump.TypeName(m));
                    var death = m.TryCast<Il2CppSauron.AIDeath>();
                    if (death != null) ReconLog.TryKeyValue("AIDeath state", () => $"attackTimer {death.attackTimer}, hasAttacked {death.hasAttacked}, despawnTime {death.despawnTime}");
                }
                ReconLog.KeyValue("live modules", string.Join(", ", names));
            });
        }

        private static void DumpModuleSettings(Il2CppSauron.AIModuleSettings m)
        {
            var d = m.TryCast<Il2CppSauron.DeathSettings>();
            if (d != null)
                ReconLog.TryKeyValue("DeathSettings", () => $"killTime {d.killTime}, killDistance {d.killDistance}, despawn {d.despawn}, " +
                                                            $"despawnTime {DescribeSpecFloat(d.despawnTime)}");
            var a = m.TryCast<Il2CppSauron.DeathAnimationSettings>();
            if (a != null)
                ReconLog.TryKeyValue("DeathAnimationSettings", () => $"idle `{a.idle}`, falls {(a.fallDirections == null ? 0 : a.fallDirections.Length)}, crossfade {a.crossfadeTime}, " +
                                                                     $"timeScaleTarget {a.timeScaleTarget}, fxBinding `{a.fxBinding}`");
            var dis = m.TryCast<Il2CppSauron.DissolveSettings>();
            if (dis != null)
                ReconLog.TryKeyValue("DissolveSettings", () => $"in {dis.inTime}, out {dis.outTime}, fx {dis.fxTime}, allRenderers {dis.useAllRenderers}, " +
                                                               $"renderer `{dis.rendererTransform}`, source shader {(Interop.Alive(dis.sourceShader) ? dis.sourceShader.name : "null")}, " +
                                                               $"dissolve shader {(Interop.Alive(dis.dissolveShader) ? dis.dissolveShader.name : "null")}");
        }

        private static string DescribeSpecFloat(object o)
        {
            if (o == null) return "null";
            try
            {
                var sb = new StringBuilder();
                foreach (var p in o.GetType().GetProperties())
                {
                    if (p.DeclaringType == typeof(Il2CppSystem.Object) || p.GetIndexParameters().Length > 0) continue;
                    if (p.Name == "Pointer" || p.Name == "ObjectClass" || p.Name == "WasCollected") continue;
                    try { sb.Append($"{p.Name}={p.GetValue(o)} "); } catch { }
                }
                return sb.ToString().Trim();
            }
            catch { return o.ToString(); }
        }

        private static void DumpRenderers(Transform root)
        {
            ReconLog.Try("renderers", () =>
            {
                var rs = root.GetComponentsInChildren<Renderer>(true);
                ReconLog.KeyValue("renderers", rs == null ? 0 : rs.Length);
                if (rs == null) return;
                foreach (var r in rs)
                {
                    if (!Interop.Alive(r)) continue;
                    ReconLog.Line($"- {HierarchyDump.TypeName(r)} `{Interop.ScenePath(r.transform)}` enabled {r.enabled}, active {r.gameObject.activeInHierarchy}, " +
                                  $"shadows {r.shadowCastingMode}, visible {SafeVisible(r)}, layer {r.gameObject.layer}");
                    var smr = r.TryCast<SkinnedMeshRenderer>();
                    if (smr != null) HierarchyDump.SkinnedMesh(smr);
                    else ReconLog.Try("materials", () => HierarchyDump.Materials(r));
                }
            });
        }

        private static string SafeVisible(Renderer r) { try { return r.isVisible.ToString(); } catch { return "?"; } }

        private static void DumpColliders(Transform root)
        {
            ReconLog.Try("colliders", () =>
            {
                var cs = root.GetComponentsInChildren<Collider>(true);
                ReconLog.KeyValue("colliders", cs == null ? 0 : cs.Length);
                if (cs == null) return;
                foreach (var c in cs)
                {
                    if (!Interop.Alive(c)) continue;
                    string shape = HierarchyDump.TypeName(c);
                    try
                    {
                        var cap = c.TryCast<CapsuleCollider>(); if (cap != null) shape += $" r {cap.radius:0.###} h {cap.height:0.###} dir {cap.direction}";
                        var sph = c.TryCast<SphereCollider>(); if (sph != null) shape += $" r {sph.radius:0.###}";
                        var box = c.TryCast<BoxCollider>(); if (box != null) shape += $" size {Interop.Vec(box.size)}";
                    }
                    catch { }
                    ReconLog.Line($"- {shape} `{Interop.ScenePath(c.transform)}` trigger {c.isTrigger}, enabled {c.enabled}, layer {c.gameObject.layer} ({LayerMask.LayerToName(c.gameObject.layer)})");
                }
            });
        }

        private static void DumpAnimators(Transform root)
        {
            ReconLog.Try("animators", () =>
            {
                foreach (var an in root.GetComponentsInChildren<Animator>(true))
                {
                    if (!Interop.Alive(an)) continue;
                    ReconLog.Line($"#### Animator `{Interop.ScenePath(an.transform)}`");
                    ReconLog.TryKeyValue("isHuman / avatar", () => $"{an.isHuman} / {(Interop.Alive(an.avatar) ? an.avatar.name : "null")}");
                    ReconLog.TryKeyValue("cullingMode / updateMode / applyRootMotion", () => $"{an.cullingMode} / {an.updateMode} / {an.applyRootMotion}");
                    ReconLog.TryKeyValue("controller", () => Interop.Alive(an.runtimeAnimatorController) ? an.runtimeAnimatorController.name : "null");
                    ReconLog.Try("layers", () =>
                    {
                        if (!an.isActiveAndEnabled) { ReconLog.Line("- (inactive: no layers or parameters to read)"); return; }
                        for (var i = 0; i < an.layerCount; i++)
                        {
                            var st = an.GetCurrentAnimatorStateInfo(i);
                            ReconLog.Line($"- layer {i} `{an.GetLayerName(i)}` weight {an.GetLayerWeight(i):0.##}, now hash {st.fullPathHash} short {st.shortNameHash} t {st.normalizedTime:0.##} len {st.length:0.##}");
                        }
                        var ps = an.parameters;
                        if (ps != null && ps.Length > 0)
                        {
                            var sb = new StringBuilder();
                            foreach (var p in ps) sb.Append($"{p.name}:{p.type} ");
                            ReconLog.KeyValue("parameters", sb.ToString().Trim());
                        }
                    });
                    ReconLog.Try("clips", () =>
                    {
                        var ctrl = an.runtimeAnimatorController;
                        if (!Interop.Alive(ctrl)) return;
                        var clips = ctrl.animationClips;
                        ReconLog.KeyValue("clips", clips == null ? 0 : clips.Length);
                        if (clips == null) return;
                        var seen = new HashSet<string>();
                        foreach (var c in clips)
                        {
                            if (!Interop.Alive(c) || !seen.Add(c.name)) continue;
                            var ev = new List<string>();
                            try { foreach (var e in c.events) ev.Add($"{e.functionName}({e.stringParameter}{(e.intParameter != 0 ? "," + e.intParameter : "")})@{e.time:0.##}"); } catch { }
                            ReconLog.Line($"  - `{c.name}` {c.length:0.##} s{(c.isLooping ? " loop" : "")}{(ev.Count > 0 ? "  events: " + string.Join(" ", ev) : "")}");
                        }
                    });
                }
            });
        }

        // ------------------------------------------------------------------ Keypad8
        public static void ToggleSampling()
        {
            _sampling = !_sampling;
            _nextSample = 0f;
            LastState.Clear();
            if (_sampling) ReconLog.Section("Death live samples (Keypad8)");
            ReconLog.Headline(_sampling ? "Enemy recon: sampling live Death once a second (Keypad8 again to stop)." : "Enemy recon: sampling stopped.");
        }

        public static void Tick()
        {
            if (!_sampling) return;
            var now = Time.unscaledTime;
            if (now < _nextSample) return;
            _nextSample = now + 1f;
            try
            {
                var head = HeadPos();
                LocalDeathCheck(head);
                foreach (var ai in Live())
                {
                    if (!IsDeath(ai)) continue;
                    Sample(ai, head);
                }
            }
            catch (Exception e) { ReconLog.Error("sample", e); }
        }

        private static void Sample(AI ai, Vector3 head)
        {
            var t = ai.transform;
            var pos = t.position;
            string floor = "none within 30 m";
            try { if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)) floor = $"{hit.distance - 0.5f:0.00} m above `{hit.collider.name}`"; } catch { }
            string state = "?";
            try
            {
                var an = ai.references.animator;
                if (Interop.Alive(an)) { var st = an.GetCurrentAnimatorStateInfo(0); state = $"{st.shortNameHash} t {st.normalizedTime:0.00} speed {an.speed:0.##}"; }
            }
            catch { }
            string inside = "?";
            try
            {
                var own = new HashSet<IntPtr>();
                foreach (var c in ai.GetComponentsInChildren<Collider>(true)) own.Add(c.Pointer);
                var hits = Physics.OverlapSphere(pos + Vector3.up * 1f, 0.25f, ~0, QueryTriggerInteraction.Ignore);
                var names = new List<string>();
                if (hits != null) foreach (var h in hits) if (Interop.Alive(h) && !own.Contains(h.Pointer)) names.Add(h.name);
                inside = names.Count == 0 ? "clear" : "in " + string.Join(", ", names);
            }
            catch { }
            string visible = "?";
            try { var any = false; foreach (var r in ai.GetComponentsInChildren<Renderer>(false)) if (r.isVisible) { any = true; break; } visible = any.ToString(); } catch { }
            string attack = "";
            try
            {
                foreach (var m in ai.modules)
                {
                    var d = m?.TryCast<Il2CppSauron.AIDeath>();
                    if (d != null) attack = $", attackTimer {d.attackTimer:0.00} hasAttacked {d.hasAttacked}";
                }
            }
            catch { }
            float hp = -1f; try { hp = ai.health.HP.Value; } catch { }
            ReconLog.Line($"- {Time.unscaledTime:0.0}s `{ai.name}` pos {Interop.Vec(pos)} vel {Interop.Vec(ai.Velocity)} floor {floor}, {inside}, " +
                          $"state {state}, visible {visible}, alive {ai.isAlive} hp {hp:0.#}, {Vector3.Distance(pos, head):0.00} m from you{attack}");
        }

        /// <summary>The moment you die, how far the nearest Death was: checks the kill distance.</summary>
        private static void LocalDeathCheck(Vector3 head)
        {
            bool dead = false;
            try { var p = AvatarPlayer.LocalAvatar; dead = Interop.Alive(p) && p.IsDead; } catch { }
            if (dead && !_wasDead)
            {
                var best = float.MaxValue;
                foreach (var ai in Live()) if (IsDeath(ai)) best = Mathf.Min(best, Vector3.Distance(ai.transform.position, head));
                ReconLog.Headline(best < float.MaxValue ? $"Enemy recon: you died with Death {best:0.00} m from your head." : "Enemy recon: you died (no Death alive).");
            }
            _wasDead = dead;
        }

        private static void AEFX_Postfix(AI __instance, string fx)
        {
            if (!_sampling) return;
            try { if (IsDeath(__instance)) ReconLog.Line($"  - {Time.unscaledTime:0.00}s AE_FX(`{fx}`) in state {StateOf(__instance)}"); } catch { }
        }

        private static void AEFootstep_Postfix(AI __instance)
        {
            if (!_sampling) return;
            try { if (IsDeath(__instance)) ReconLog.Line($"  - {Time.unscaledTime:0.00}s AE_Footstep in state {StateOf(__instance)}"); } catch { }
        }

        private static string StateOf(AI ai)
        {
            try { var an = ai.references.animator; var st = an.GetCurrentAnimatorStateInfo(0); return $"{st.shortNameHash} t {st.normalizedTime:0.00}"; }
            catch { return "?"; }
        }

        // ------------------------------------------------------------------ Keypad9
        public static void DumpLighting()
        {
            ReconLog.Section("Lighting (Keypad9)");
            ReconLog.TryKeyValue("scene", () => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            var cam = Camera.main;
            ReconLog.TryKeyValue("camera", () => Interop.Alive(cam) ? $"`{Interop.ScenePath(cam.transform)}` HDR {cam.allowHDR}, path {cam.actualRenderingPath}, far {cam.farClipPlane}" : "none");
            if (Interop.Alive(cam)) ReconLog.TryKeyValue("camera components", () => HierarchyDump.ComponentList(cam.transform));
            ReconLog.Try("bloom", () =>
            {
                var blooms = UnityEngine.Object.FindObjectsOfType<FastBloom>(true);
                ReconLog.KeyValue("FastBloom instances", blooms == null ? 0 : blooms.Length);
                if (blooms != null)
                    foreach (var b in blooms)
                        ReconLog.Line($"- `{Interop.ScenePath(b.transform)}` enabled {b.isActiveAndEnabled}, amount {b.BloomAmount}, threshold {b.BloomThreshold}, softness {b.BloomSoftness}, iterations {b.BloomIterations}");
            });
            ReconLog.TryKeyValue("ambient", () => $"mode {RenderSettings.ambientMode}, light {RenderSettings.ambientLight}, sky {RenderSettings.ambientSkyColor}, " +
                                                  $"equator {RenderSettings.ambientEquatorColor}, ground {RenderSettings.ambientGroundColor}, intensity {RenderSettings.ambientIntensity}");
            ReconLog.TryKeyValue("fog", () => RenderSettings.fog ? $"{RenderSettings.fogMode} colour {RenderSettings.fogColor} density {RenderSettings.fogDensity} {RenderSettings.fogStartDistance}-{RenderSettings.fogEndDistance}" : "off");
            ReconLog.TryKeyValue("light probes", () => LightmapSettings.lightProbes == null ? "none" : $"{LightmapSettings.lightProbes.count} probes");
            ReconLog.TryKeyValue("lightmaps", () => LightmapSettings.lightmaps == null ? 0 : LightmapSettings.lightmaps.Length);
            ReconLog.Try("bakery volumes", () =>
            {
                var vols = UnityEngine.Object.FindObjectsOfType<BakeryVolume>(true);
                ReconLog.KeyValue("BakeryVolume instances", vols == null ? 0 : vols.Length);
                ReconLog.KeyValue("BakeryVolume.globalVolume", Interop.Alive(BakeryVolume.globalVolume) ? Interop.ScenePath(BakeryVolume.globalVolume.transform) : "null");
            });
            var head = HeadPos();
            ReconLog.Try("lights within 15 m", () =>
            {
                var lights = UnityEngine.Object.FindObjectsOfType<Light>();
                var n = 0;
                if (lights != null)
                    foreach (var l in lights)
                    {
                        if (!Interop.Alive(l) || !l.isActiveAndEnabled) continue;
                        var d = l.type == LightType.Directional ? 0f : Vector3.Distance(l.transform.position, head);
                        if (d > 15f) continue;
                        n++;
                        string bake = "?"; try { bake = l.bakingOutput.lightmapBakeType.ToString(); } catch { }
                        ReconLog.Line($"- {l.type} `{Interop.ScenePath(l.transform)}` {d:0.0} m, range {l.range:0.#}, intensity {l.intensity:0.##}, colour {l.color}, shadows {l.shadows}, mode {l.renderMode}, baked {bake}");
                    }
                ReconLog.KeyValue("realtime lights within 15 m", n);
            });
            ReconLog.Try("nearest enemy material", () =>
            {
                AI nearest = null; var best = float.MaxValue;
                foreach (var ai in Live()) { var d = Vector3.Distance(ai.transform.position, head); if (d < best) { best = d; nearest = ai; } }
                if (nearest == null) { ReconLog.Line("- no live enemy"); return; }
                ReconLog.Line($"- `{nearest.name}`, {best:0.0} m");
                foreach (var r in nearest.GetComponentsInChildren<Renderer>(false)) { if (Interop.Alive(r) && r.enabled) { HierarchyDump.Materials(r); break; } }
            });
            ReconLog.Try("your body material", () =>
            {
                var p = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(p) || !Interop.Alive(p.FullBody)) return;
                foreach (var smr in p.FullBody.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { if (Interop.Alive(smr)) { HierarchyDump.Materials(smr); break; } }
            });
            ReconLog.Headline($"Enemy recon: lighting written to {ReconLog.CurrentFile}");
        }
    }
}
