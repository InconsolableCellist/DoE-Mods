using System;
using Il2Cpp;
using AI = Il2CppSauron.AI;
using Il2CppPhoton.Pun;
using UnityEngine;

namespace LootOverhaul.Recon
{
    /// <summary>
    /// Read-only postfixes on the gameplay methods the design intends to build on. Each one
    /// answers a "verify first" item from docs/LOOT-OVERHAUL.md by logging what actually
    /// happens, on which client, in what order. No prefix here ever skips the original.
    /// </summary>
    public static class GameplayHooks
    {
        private static float _lastDamageLog;

        public static void Install()
        {
            var t = typeof(GameplayHooks);
            // 4. enemy death — the drop-roll hook. The [PunRPC] overload (int, int).
            Hooks.Patch(typeof(AI), "OnKilled", null, Hooks.Of(t, nameof(AI_OnKilled)), paramCount: 2);
            // chest loot — the bonus-roll hook. NOT Chest.OnLootCollected: its base body is empty
            // and shares the universal stub address (the 0.1.0 crash). These two have real bodies.
            Hooks.Patch(typeof(Chest), "EV_ChestOpened", null, Hooks.Of(t, nameof(Chest_Opened)));
            Hooks.Patch(typeof(Chest), "EV_CollectedLoot", null, Hooks.Of(t, nameof(Chest_CollectedLoot)));
            // 3. pickup / drop — where "bag instead of wield" will go.
            Hooks.Patch(typeof(Prop), "PickUp", null, Hooks.Of(t, nameof(Prop_PickUp)));
            Hooks.Patch(typeof(Prop), "Drop", null, Hooks.Of(t, nameof(Prop_Drop)));
            // 6. holster fill order relative to respawn.
            Hooks.Patch(typeof(Holster), "InitHolsterContents", null, Hooks.Of(t, nameof(Holster_Init)));
            Hooks.Patch(typeof(Holster), "RefillHolster", null, Hooks.Of(t, nameof(Holster_Refill)));
            Hooks.Patch(typeof(Holster), "OnAvatarRespawn", null, Hooks.Of(t, nameof(Holster_OnAvatarRespawn)));
            Hooks.Patch(typeof(AvatarPlayer), "RespawnAvatar", null, Hooks.Of(t, nameof(Avatar_Respawn)));
            Hooks.Patch(typeof(AvatarPlayer), "RespawnLocalPlayer", null, Hooks.Of(t, nameof(Avatar_RespawnLocal)));
            // 7. damage entry points for the armor question.
            Hooks.Patch(typeof(AvatarPlayer), "OnDamaged", null, Hooks.Of(t, nameof(Avatar_OnDamaged)));
            Hooks.Patch(typeof(AvatarPlayer), "ApplyRemoteDamage", null, Hooks.Of(t, nameof(Avatar_ApplyRemoteDamage)));
        }

        private static string Net()
        {
            try { return PhotonNetwork.InRoom ? (PhotonNetwork.IsMasterClient ? "MASTER" : "client") : "no-room"; }
            catch { return "?"; }
        }

        private static void AI_OnKilled(AI __instance, int __0, int __1)
        {
            try
            {
                var pos = Interop.Alive(__instance) ? Interop.Vec(__instance.transform.position) : "?";
                var name = Interop.Alive(__instance) ? __instance.name : "<dead proxy>";
                string type = "?", armor = "?";
                try { type = __instance.type.ToString(); armor = __instance.armorTier.ToString(); } catch { }
                ReconLog.Line($"AI.OnKilled [{Net()}] `{name}` type={type} armorTier={armor} killerActor={__0} damageType={__1} at {pos}");
            }
            catch (Exception e) { ReconLog.Error("AI.OnKilled hook", e); }
        }

        private static void Chest_Opened(Chest __instance) => LogChest("EV_ChestOpened", __instance);
        private static void Chest_CollectedLoot(Chest __instance) => LogChest("EV_CollectedLoot", __instance);

        private static void LogChest(string what, Chest chest)
        {
            try
            {
                if (!Interop.Alive(chest)) { ReconLog.Line($"Chest.{what} [{Net()}] <dead instance>"); return; }
                string avail = "?", lootType = "?", boss = "?";
                try { avail = chest.availLoot.ToString(); lootType = chest.lootType.ToString(); boss = chest.IsBossChest.ToString(); } catch { }
                ReconLog.Line($"Chest.{what} [{Net()}] `{chest.name}` availLoot={avail} lootType={lootType} boss={boss} at {Interop.Vec(chest.transform.position)}");
            }
            catch (Exception e) { ReconLog.Error($"Chest.{what} hook", e); }
        }

        private static string DescribeProp(Prop p)
        {
            if (!Interop.Alive(p)) return "<dead>";
            var s = $"`{p.name}` type={p.type}";
            try
            {
                var w = p.TryCast<Weapon>();
                if (w != null) s += $" weapon seed={w.RandomSeed} random={w.isRandomlyGenerated}";
            }
            catch { }
            try
            {
                var pv = p.GetComponent<PhotonView>();
                if (Interop.Alive(pv)) s += $" view={pv.ViewID} mine={pv.IsMine}";
            }
            catch { }
            return s;
        }

        private static void Prop_PickUp(Prop __instance) => ReconLog.Line($"Prop.PickUp [{Net()}] {DescribeProp(__instance)}");
        private static void Prop_Drop(Prop __instance) => ReconLog.Line($"Prop.Drop [{Net()}] {DescribeProp(__instance)}");

        private static void Holster_Init(Holster __instance, bool __0)
        {
            try
            {
                var hazards = -1;
                try { var hw = Holster.hazardWeapons; hazards = hw == null ? 0 : hw.Count; } catch { }
                ReconLog.Line($"Holster.InitHolsterContents(isLobby={__0}) holster#{__instance.index} `{Interop.Name(__instance)}` " +
                              $"mode={__instance.positioningMode} inventory={__instance.isInventoryHolster} hazardWeapons={hazards}");
            }
            catch (Exception e) { ReconLog.Error("Holster.InitHolsterContents hook", e); }
        }

        private static void Holster_Refill(Holster __instance) =>
            ReconLog.Line($"Holster.RefillHolster holster#{SafeIndex(__instance)} `{Interop.Name(__instance)}`");

        private static void Holster_OnAvatarRespawn(Holster __instance) =>
            ReconLog.Line($"Holster.OnAvatarRespawn holster#{SafeIndex(__instance)} `{Interop.Name(__instance)}`");

        private static int SafeIndex(Holster h) { try { return h.index; } catch { return -1; } }

        private static void Avatar_Respawn(AvatarPlayer __instance) =>
            ReconLog.Line($"AvatarPlayer.RespawnAvatar [{Net()}] `{Interop.Name(__instance)}` local={SafeIsLocal(__instance)}");

        private static void Avatar_RespawnLocal(AvatarPlayer __instance, bool __0) =>
            ReconLog.Line($"AvatarPlayer.RespawnLocalPlayer(initialSpawn={__0}) `{Interop.Name(__instance)}`");

        private static string SafeIsLocal(AvatarPlayer a)
        {
            try { return (AvatarPlayer.LocalAvatar != null && a.Pointer == AvatarPlayer.LocalAvatar.Pointer).ToString(); }
            catch { return "?"; }
        }

        private static void Avatar_OnDamaged(AvatarPlayer __instance, float __0, float __1, Vector3 __2, DamageType __3, bool __result)
        {
            if (Time.unscaledTime - _lastDamageLog < 0.25f) return;
            _lastDamageLog = Time.unscaledTime;
            try
            {
                var hp = "?";
                try { hp = __instance.health.normalizedHP.ToString("0.00"); } catch { }
                ReconLog.Line($"AvatarPlayer.OnDamaged(dmg={__0:0.#}, kb={__1:0.#}, type={__3}) -> {__result}, hp={hp} `{Interop.Name(__instance)}`");
            }
            catch (Exception e) { ReconLog.Error("OnDamaged hook", e); }
        }

        private static void Avatar_ApplyRemoteDamage(AvatarPlayer __instance, float __0, float __1, Vector3 __2, DamageType __3, int __4) =>
            ReconLog.Line($"AvatarPlayer.ApplyRemoteDamage(dmg={__0:0.#}, type={__3}, actor={__4}) `{Interop.Name(__instance)}`");
    }
}
