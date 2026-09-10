using System;
using Descent.Recon;
using Descent.Run;
using Il2Cpp;

namespace Descent.Dungeon
{
    /// <summary>
    /// Per-floor banking through the game's own end-of-mission code, in the order the game
    /// runs it across the lobby transition (traced 2026-09-07):
    /// <c>SetRewardStats</c> (party stats from the current dungeon's bonus, hazards and AI
    /// tier) → <c>SaveLoot</c> (XP and gold onto the profile, level-ups, achievements, the
    /// PlayFab save, leaderboards) → <c>InitLocalPlayerShareableStats</c> (the per-mission
    /// counters reset, as the hologram screen does after saving).
    /// Nothing here invents a number; it asks the game to settle up before the next floor.
    /// </summary>
    public static class Rewards
    {
        public static int Banked { get; private set; }
        public static string LastSummary { get; private set; } = "(none)";

        public static string DescribeStats(GameManager.PlayerStatDef s)
        {
            if (s == null) return "<null>";
            try
            {
                return $"kills {s.kills.Value} coins {s.coins.Value} deaths {s.deaths.Value} revives {s.revives.Value} chests {s.chests.Value} breakables {s.breakables.Value} " +
                       $"questXP {s.questXP.Value} perfXP {s.performanceXP.Value} questGold {s.questGold.Value} lootedGold {s.lootedGold.Value} mult {s.bonusMult.Value:0.00} " +
                       $"modules {s.weaponModules.Value} initialXP {s.initialXP.Value} initialLevel {s.initialLevel.Value}";
            }
            catch (Exception e) { return $"<stats unreadable: {e.GetType().Name}>"; }
        }

        /// <summary>Bank the floor that just ended. Every client does this for itself.</summary>
        public static bool Bank(FloorSpec floor)
        {
            var gm = GameManager.Instance;
            if (gm == null) { Core.Log.Warning("Bank: no GameManager."); return false; }
            var local = AvatarPlayer.LocalAvatar;
            var end = "?";
            try { end = GameManager.MissionEndState.ToString(); } catch { }
            ReconLog.Section($"Banking floor {floor.Number} (end state {end})");
            int xpBefore = -1, levelBefore = -1;
            try { xpBefore = PlayerProfile.GetXP(); levelBefore = PlayerProfile.GetLevel(); } catch { }
            ReconLog.Line($"profile before: XP {xpBefore} level {levelBefore}; dungeon {FloorPlan.Describe(GameManager.CurrentDungeon)}");

            try { UIEndMission.SetCurrentDungeon(GameManager.CurrentDungeon, GameManager.DifficultyTier); }
            catch (Exception e) { ReconLog.Line($"UIEndMission.SetCurrentDungeon threw {e.GetType().Name}: {e.Message}"); }

            try { gm.SetRewardStats(); }
            catch (Exception e) { Core.Log.Error($"SetRewardStats threw: {e.GetType().Name}: {e.Message}"); return false; }

            GameManager.PlayerStatDef stats = null;
            try { if (local != null) stats = gm.FindPlayerStatDef(local); } catch (Exception e) { ReconLog.Line($"FindPlayerStatDef threw {e.GetType().Name}"); }
            ReconLog.Line($"party stats for me: {DescribeStats(stats)}");
            if (stats == null)
            {
                Core.Log.Warning("Bank: no stats for the local player after SetRewardStats; SaveLoot would log an error, skipping this floor's banking.");
                return false;
            }

            try { gm.SaveLoot(); }
            catch (Exception e) { Core.Log.Error($"SaveLoot threw: {e.GetType().Name}: {e.Message}"); return false; }

            // The per-mission counters reset, as the hologram screen does after saving. The party
            // stats table is deliberately NOT cleared: vanilla clears it only on the way to the hub
            // (EV_ReturnToLobby) and after a scene finishes initialising; the dungeon load screen reads
            // it for its player slots, and an empty table there threw five exceptions a frame for the
            // whole of floor 2 on 2026-09-07.
            try { local?.InitLocalPlayerShareableStats(); }
            catch (Exception e) { ReconLog.Line($"InitLocalPlayerShareableStats threw {e.GetType().Name}: {e.Message}"); }
            try { if (local != null) ReconLog.Line($"stats after reset: {DescribeStats(gm.FindPlayerStatDef(local))}"); } catch { }

            int xpAfter = -1, levelAfter = -1;
            try { xpAfter = PlayerProfile.GetXP(); levelAfter = PlayerProfile.GetLevel(); } catch { }
            var xpGain = xpAfter >= 0 && xpBefore >= 0 ? xpAfter - xpBefore : -1;
            int gold = -1; try { gold = stats.questGold.Value + stats.lootedGold.Value; } catch { }
            Banked++;
            LastSummary = $"floor {floor.Number}: XP {xpBefore} -> {xpAfter} (+{xpGain}), level {levelBefore} -> {levelAfter}, gold +{gold}";
            ReconLog.Headline($"Banked {LastSummary}");
            Toast($"Floor {floor.Number} banked: <color=#F5C542>+{Math.Max(0, xpGain)} XP</color>{(gold > 0 ? $"  +{gold} gold" : "")}{(levelAfter > levelBefore && levelBefore >= 0 ? $"  LEVEL {levelAfter}!" : "")}");
            return true;
        }

        public static void Toast(string text)
        {
            try { FXNotifications.AddQuickNotification(text, 0f); }
            catch (Exception e) { Core.Log.Msg($"[toast] {text} (notification failed: {e.GetType().Name})"); }
        }
    }
}
