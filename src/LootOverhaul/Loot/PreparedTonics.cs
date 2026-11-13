using System;
using System.Collections.Generic;
using Il2Cpp;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>One tonic ordered at the kobold for the next run. Saved with the bag.</summary>
    public class PreparedTonic
    {
        public string Stat;
        public int Tier;
        /// <summary>Tokens paid, refunded in full on a revert (so a later price change never short-changes anyone).</summary>
        public long Price;
    }

    /// <summary>
    /// Tonics to order (0.10.10). Buying a tonic at the kobold no longer puts a bottle in the bag:
    /// it is prepared for the next run, one per stat, and every prepared tonic is drunk on its own
    /// when the next dungeon starts (not in the lobby, the practice arena or the tutorial). A
    /// prepared tonic can be reverted at the booth for a full refund until then.
    ///
    /// The run start is the scene load the mod already watches (<see cref="Core"/>'s
    /// OnSceneWasInitialized, the same place the lobby clears the tonic table). The apply waits
    /// for the gate and the local exosuit and avatar, checking once a second, and runs once per
    /// run. If they never come (a vanilla room), nothing is spent: the tonics stay prepared.
    /// </summary>
    public static class PreparedTonics
    {
        private const float FirstTry = 1.5f;
        private const float RetryEvery = 1f;
        private const int MaxTries = 60;

        private static float _applyAt = -1f;
        private static int _tries;
        private static string _scene;

        public static List<PreparedTonic> List => BagManager.Inventory.PreparedTonics;

        public static Buffs.Def DefFor(string stat) => stat == LightPotion.Stat ? LightPotion.Def : Buffs.Find(stat);

        public static PreparedTonic Get(string stat)
        {
            foreach (var p in List) if (p.Stat == stat) return p;
            return null;
        }

        public static long TotalCost()
        {
            long t = 0;
            foreach (var p in List) t += p.Price;
            return t;
        }

        public static string NameOf(PreparedTonic p)
        {
            var d = DefFor(p.Stat);
            var tier = Math.Max(0, Math.Min(2, p.Tier));
            return $"{Buffs.TierNames[tier]} {(d == null ? p.Stat : d.Name)}";
        }

        /// <summary>Order a tonic for the next run. One per stat: a stat already prepared must be reverted first.</summary>
        public static bool Prepare(Buffs.Def def, int tier)
        {
            var inv = BagManager.Inventory;
            if (def == null) return false;
            tier = Math.Max(0, Math.Min(2, tier));
            if (def.Stat != LightPotion.Stat && !Unlocks.PerkUnlocked(def.Stat)) { BagManager.Toast("You haven't unlocked that perk yet."); return false; }
            var have = Get(def.Stat);
            if (have != null) { BagManager.Toast($"{NameOf(have)} is already prepared. Revert it first to pick another strength."); return false; }
            var price = Shop.TonicPrice(def, tier);
            if (inv.Gold < price) { BagManager.Toast($"Not enough tokens: {price} needed, you have {inv.Gold}."); return false; }
            inv.Gold -= price;
            var p = new PreparedTonic { Stat = def.Stat, Tier = tier, Price = price };
            inv.PreparedTonics.Add(p);
            inv.Save();
            Shop.Bought++;
            ReconLog.Line($"shop: prepared tonic {NameOf(p)} for {price} -> tokens {inv.Gold}");
            BagManager.Toast($"{NameOf(p)} prepared: you drink it when your next run starts.");
            return true;
        }

        /// <summary>Take back a prepared tonic for everything that was paid for it.</summary>
        public static bool Revert(string stat)
        {
            var inv = BagManager.Inventory;
            var p = Get(stat);
            if (p == null) return false;
            inv.PreparedTonics.Remove(p);
            inv.Gold += p.Price;
            inv.Save();
            ReconLog.Line($"shop: reverted tonic {NameOf(p)}, refunded {p.Price} -> tokens {inv.Gold}");
            BagManager.Toast($"{NameOf(p)} reverted: {p.Price} tokens back.");
            return true;
        }

        public static bool RevertAll()
        {
            var inv = BagManager.Inventory;
            if (inv.PreparedTonics.Count == 0) return false;
            var n = inv.PreparedTonics.Count;
            var refund = TotalCost();
            inv.PreparedTonics.Clear();
            inv.Gold += refund;
            inv.Save();
            ReconLog.Line($"shop: reverted all {n} prepared tonic(s), refunded {refund} -> tokens {inv.Gold}");
            BagManager.Toast($"All {n} prepared tonic(s) reverted: {refund} tokens back.");
            return true;
        }

        /// <summary>From Core.OnSceneWasInitialized: a dungeon scene arms the one apply for this run.</summary>
        public static void OnSceneChanged(string sceneName)
        {
            _applyAt = -1f;
            _tries = 0;
            _scene = sceneName;
            if (NotARun(sceneName)) return;
            _applyAt = UnityEngine.Time.unscaledTime + FirstTry;
        }

        /// <summary>Every frame from Core; one float compare unless an apply is due.</summary>
        public static void Tick()
        {
            if (_applyAt < 0f || UnityEngine.Time.unscaledTime < _applyAt) return;
            _applyAt = -1f;
            try { TryApply(); }
            catch (Exception e) { Core.Log.Warning($"Prepared tonics: apply threw {e.GetType().Name}: {e.Message}"); }
        }

        private static void TryApply()
        {
            if (NotARun(_scene)) return;
            List<PreparedTonic> list;
            try { list = BagManager.Inventory.PreparedTonics; } catch { list = null; }
            if (list == null || list.Count == 0) return;

            var ready = ModGate.Active;
            if (ready) { try { ready = AvatarPlayer.LocalExoSuit != null && Interop.Alive(AvatarPlayer.LocalAvatar); } catch { ready = false; } }
            if (!ready)
            {
                if (++_tries < MaxTries) { _applyAt = UnityEngine.Time.unscaledTime + RetryEvery; return; }
                Core.Log.Msg($"prepared tonics: {list.Count} kept for a later run (no modded room or no local player in '{_scene}' after {MaxTries} s)");
                return;
            }

            Buffs.Tick();   // the postfix's local exosuit pointer, fresh for this scene
            var names = new List<string>();
            foreach (var p in list.ToArray())
            {
                var def = DefFor(p.Stat);
                if (def == null) { Core.Log.Warning($"prepared tonics: unknown stat {p.Stat}, dropped"); continue; }
                var tier = Math.Max(0, Math.Min(2, p.Tier));
                var mult = def.Mults[tier];
                var name = NameOf(p);
                // One per stat, the strongest wins: a stronger one already drunk this run keeps its place.
                if (Buffs.Activate(p.Stat, mult, $"prepared {name}")) names.Add(name);
                else names.Add($"{name} (a stronger one is already active)");
            }
            list.Clear();
            BagManager.Inventory.Save();
            Core.Log.Msg($"prepared tonics applied in '{_scene}': {string.Join(", ", names)}");
            BagManager.Toast($"Tonics active: {string.Join(", ", names)}");
            BagPanel.Refresh();
        }

        /// <summary>Lobby, main menu, practice arena or tutorial: no tonic is drunk there.</summary>
        private static bool NotARun(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return true;
            if (Is(scene, GameManager.LOBBY_SCENE) || Is(scene, GameManager.MEGALOBBY_SCENE)
                || Is(scene, GameManager.MAINMENU_SCENE) || Is(scene, GameManager.SANDBOX_SCENE)
                || Is(scene, GameManager.TUTORIAL_SCENE)) return true;
            if (scene.IndexOf("tutorial", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            try { if (GameManager.IsSandboxScene) return true; } catch { }
            try { if (GameManager.IsTutorialScene) return true; } catch { }
            return false;
        }

        private static bool Is(string scene, string constant) =>
            !string.IsNullOrEmpty(constant) && string.Equals(scene, constant, StringComparison.OrdinalIgnoreCase);
    }
}
