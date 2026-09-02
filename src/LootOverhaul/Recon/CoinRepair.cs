using System;
using Il2Cpp;
using Il2CppPhoton.Pun;
using MelonLoader;
using UnityEngine;

namespace LootOverhaul.Recon
{
    /// <summary>
    /// One-shot, explicitly authorised profile write, and the only one this mod will ever
    /// make. The 0.1.0 test button inherited the fabricator's click handler and charged
    /// 9,999 coins twice (2026-09-02, recon-20260902-002755.md). The owner asked for the
    /// balance to be restored. <c>[LootOverhaul_Dev] CoinRepairAmount</c> is added to Coins
    /// once, in the lobby, after the profile has settled; the entry is then reset to 0 and
    /// this file is deleted in the next build. Every step is logged, and the watchdog is told
    /// so the write is attributed honestly.
    /// </summary>
    public static class CoinRepair
    {
        private static bool _done;
        private static float _lobbyAt = -1f;

        public static void OnScene(string sceneName) =>
            _lobbyAt = sceneName == GameManager.LOBBY_SCENE ? Time.unscaledTime : -1f;

        public static void Tick()
        {
            if (_done) return;
            var amount = ModConfig.CoinRepairAmount.Value;
            if (amount == 0) { _done = true; return; }
            if (_lobbyAt < 0f || Time.unscaledTime - _lobbyAt < 8f) return;
            try { if (!PhotonNetwork.InRoom) return; } catch { return; }

            _done = true;
            ProfileWatch.Probe = "coin repair (authorised one-shot)";
            try
            {
                var before = PlayerProfile.GetCharacterData(PlayerData.CharacterValues.Coins);
                ReconLog.Headline($"COIN REPAIR: balance before = {before}; applying {amount:+#;-#}");
                var ok = PlayerProfile.IncrementCharacterData(PlayerData.CharacterValues.Coins, amount);
                var after = PlayerProfile.GetCharacterData(PlayerData.CharacterValues.Coins);
                PlayerProfile.SavePlayerProfile(true, null);
                ReconLog.Headline($"COIN REPAIR: increment returned {ok}; balance after = {after}; profile save requested.");
            }
            catch (Exception e) { ReconLog.Error("coin repair", e); }
            finally
            {
                ProfileWatch.Probe = null;
                try { ModConfig.CoinRepairAmount.Value = 0; MelonPreferences.Save(); ReconLog.Headline("COIN REPAIR: CoinRepairAmount reset to 0."); }
                catch (Exception e) { ReconLog.Error("coin repair reset", e); }
            }
        }
    }
}
