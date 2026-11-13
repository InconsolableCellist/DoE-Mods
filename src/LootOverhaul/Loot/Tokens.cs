using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using LootOverhaul.Gate;
using LootOverhaul.Recon;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The run payout: the gold the dungeon end screen already reports is converted into tokens and
    /// credited to <c>BagManager.Inventory</c>.
    ///
    /// That end screen is the hook because it is the one place the game reports a run's gold, on a
    /// completed run and a wipe alike. Boss and mini-boss kills are the only signal it does not
    /// carry, so they are counted from <c>AI.OnKilled</c>, which
    /// <see cref="Recon.GameplayHooks"/> owns. Settings live in <see cref="ModConfig"/> under
    /// [LootOverhaul] and [LootOverhaul_Dev]. Nothing is credited where the gate is inert.
    /// </summary>
    public static class Tokens
    {

        // ---- per-run state
        internal static int BossKills;
        internal static int MiniKills;
        internal static int KillsSeen;
        private static bool _inRun;
        private static bool _paidThisRun;

        // photon view id -> unscaled time of the kill we already counted for it
        private static readonly Dictionary<int, float> _countedAt = new Dictionary<int, float>();
        private const float DedupWindow = 2.0f;

        // ---- run bookkeeping for the log
        private static DateTime _runStartedUtc;
        private static string _runScene = "?";
        private static long _tokensBefore;
        private static long _tokensAfter;

        // ---- payout message is reposted to keep it on screen (see ToastLong)
        private static string _toastText;
        private static float _toastUntil;
        private static float _toastNextAt;

        // ---- token balance seen when the run began, to expose shop spending between runs
        private static long _tokensAtRunStart = -1L;

        // ------------------------------------------------------------------ lifecycle
        /// <summary>
        /// Wire the run payout. Two postfixes, neither of which changes what the game returned:
        /// <c>AI.OnKilled</c> counts bosses and mini-bosses (the game itself does not track them),
        /// and <c>GameManager.CheckAchievements</c> is the one place the end screen reports the gold
        /// earned - for a completed run and for a wipe alike.
        /// </summary>
        public static void Install()
        {
            var t = typeof(Tokens);
            var patched = 0;
            patched += Hooks.Patch(typeof(Il2Cpp.GameManager), "CheckAchievements", null, Hooks.Of(t, nameof(CheckAchievements_Post)), paramCount: 4);
            Core.Log.Msg($"Tokens: payout hook {patched}/1 installed - rate {ModConfig.GoldToTokenRate.Value}, "
                + $"boss +{ModConfig.BossKillBonus.Value:P0}, mini +{ModConfig.MinibossKillBonus.Value:P0}, "
                + $"player +{ModConfig.TokenPlayerBonusPerExtraPlayer.Value:P0}, run log {(ModConfig.RunLogging.Value ? "on" : "off")}");
            if (patched < 1) Core.Log.Warning("Tokens: the payout hook is missing - the payout will not fire.");
        }

        /// <summary>Called from Core.OnUpdate; keeps the payout toast on screen.</summary>
        public static void Tick() => RepeatToast();

        /// <summary>Case-insensitive comparison of a scene name against one of the game's constants.</summary>
        private static bool SceneIs(string scene, string constant) =>
            !string.IsNullOrEmpty(scene) && !string.IsNullOrEmpty(constant)
            && string.Equals(scene, constant, StringComparison.OrdinalIgnoreCase);

        /// <summary>Called from Core.OnSceneWasInitialized.</summary>
        public static void OnSceneChanged(string sceneName)
        {
            string s = (sceneName ?? string.Empty).ToLowerInvariant();
            // Against the game's own constants, not literals: there is more than one lobby scene
            // (MEGALOBBY_SCENE), and mistaking it for a dungeon would clear the boss tally just
            // before a late end screen pays out.
            if (SceneIs(s, Il2Cpp.GameManager.LOBBY_SCENE)
                || SceneIs(s, Il2Cpp.GameManager.MEGALOBBY_SCENE)
                || SceneIs(s, Il2Cpp.GameManager.MAINMENU_SCENE)
                || SceneIs(s, Il2Cpp.GameManager.SANDBOX_SCENE))
            {
                // Kills stop counting outside a run, but the counters are deliberately NOT cleared
                // here: the game hands out the end-screen stats *after* the lobby has loaded.
                if (_inRun) Log($"run left (scene '{s}') — holding {BossKills} boss / {MiniKills} mini for the end screen");
                _inRun = false;
            }
            else
            {
                // a dungeon scene: a fresh run starts, so the previous run's tally is cleared
                if (_inRun || BossKills != 0 || MiniKills != 0)
                    Log($"new run (scene '{s}') — clearing previous tally {BossKills} boss / {MiniKills} mini, paid={_paidThisRun}");
                _inRun = true;
                _paidThisRun = false;
                BossKills = 0;
                MiniKills = 0;
                KillsSeen = 0;
                _countedAt.Clear();
                _runStartedUtc = DateTime.UtcNow;
                _runScene = s;
                long bal;
                _tokensAtRunStart = TryReadTokens(out bal) ? bal : -1L;
                Log($"run started (scene '{s}')  token balance at entry: {(_tokensAtRunStart < 0 ? "unavailable" : _tokensAtRunStart.ToString())}");
            }
        }

        // ------------------------------------------------------------------ kill counting
        public static void CountKill(Il2CppSauron.AI ai)
        {
            try
            {
                if (!_inRun || ai == null) return;
                KillsSeen++;
                int rank = BossRank(ai);
                if (rank == 0) return;

                // The game can hand the same death to this hook more than once: identical enemy,
                // identical position, 0.07-0.27s apart, sometimes with a different killerActor.
                // Count each enemy's photon view once per short window instead of once per call.
                int view = ViewIdOf(ai);
                if (view >= 0)
                {
                    float now = UnityEngine.Time.unscaledTime;
                    if (_countedAt.TryGetValue(view, out float prev) && now - prev < DedupWindow)
                    {
                        Log($"ignored a repeat report for view {view} ({now - prev:0.###}s after the first)");
                        return;
                    }
                    _countedAt[view] = now;
                    if (_countedAt.Count > 512) PruneCountedAt(now);
                }

                if (rank == 2)
                {
                    BossKills++;
                    Log($"BOSS killed  (this run: {BossKills} boss / {MiniKills} mini)");
                }
                else if (rank == 1)
                {
                    MiniKills++;
                    Log($"mini-boss killed (this run: {BossKills} boss / {MiniKills} mini)");
                }
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value) Core.Log.Warning("OnKilled count: " + e.Message);
            }
        }

        /// <summary>2 = boss, 1 = mini-boss, 0 = ordinary enemy. Same rule LootOverhaul uses.</summary>
        private static int BossRank(Il2CppSauron.AI ai)
        {
            try
            {
                int cls = (int)ai.references.spawnedAsClass;
                if (cls == 5) return 2;   // AIClass.Boss
                if (cls == 4) return 1;   // AIClass.Miniboss
            }
            catch
            {
            }
            try
            {
                if (ai.IsBoss) return 2;
            }
            catch
            {
            }
            return 0;
        }

        /// <summary>Photon view id of an enemy, or -1 when it cannot be read.</summary>
        private static int ViewIdOf(Il2CppSauron.AI ai)
        {
            try
            {
                Il2CppPhoton.Pun.PhotonView pv = ai.references.PVO;
                if (pv == null) return -1;
                return pv.ViewID;
            }
            catch
            {
                return -1;
            }
        }

        private static void PruneCountedAt(float now)
        {
            var stale = new List<int>();
            foreach (KeyValuePair<int, float> kv in _countedAt)
            {
                if (now - kv.Value > 60f) stale.Add(kv.Key);
            }
            foreach (int k in stale) _countedAt.Remove(k);
        }

        // ------------------------------------------------------------------ payout
        private static void CheckAchievements_Post(Il2Cpp.GameManager.PlayerStatDef localStats, bool success,
                                                   int goldEarned, int xpEarned)
        {
            try
            {
                if (_paidThisRun) return;
                if (localStats == null) return;

                // The same gate the rest of the mod runs behind. In a public room, or one with an
                // unmodded or mismatched peer, LootOverhaul is inert by design - so it must not
                // quietly pay tokens there either.
                if (!ModConfig.Enabled.Value || !ModGate.Active)
                {
                    Log($"payout skipped: mod {(ModConfig.Enabled.Value ? "enabled" : "disabled")}, " +
                        $"gate {(ModGate.Active ? "active" : "inert (" + ModGate.Reason + ")")} - " +
                        $"{goldEarned} gold left unconverted");
                    return;
                }

                int localView = 0;
                try { localView = Il2Cpp.AvatarPlayer.LocalAvatarViewID; } catch { }
                if (localView != 0 && localStats.viewID != localView)
                {
                    Core.Log.Warning(
                        $"end-screen stats are for view {localStats.viewID}, local avatar is {localView} — not paying this one");
                    return;
                }
                if (localView == 0)
                    Core.Log.Warning("local avatar view id unavailable — paying on the first end-screen stats received");

                _paidThisRun = true;

                // Read the room's stats once: the payout, the two sums and the per-player dump all
                // come off this one snapshot.
                List<PartyMember> party = ReadParty();
                RunContext ctx = RunContext.Read(localStats, party);

                double bossPart = BossKills * (double)ModConfig.BossKillBonus.Value;
                double miniPart = MiniKills * (double)ModConfig.MinibossKillBonus.Value;
                double hazardPart = ctx.HazardSteps * (double)ModConfig.HazardBonusPerLevel.Value;
                double tierPart = ctx.TierSteps * (double)ModConfig.TierBonusPerLevel.Value;
                double playerPart = ctx.ExtraPlayers * (double)ModConfig.TokenPlayerBonusPerExtraPlayer.Value;
                double chestPart = ctx.Chests * (double)ModConfig.TokenChestBonusPerChest.Value;
                // No malus, and no floor to bound one: bonuses only ever add. The run's own gold
                // already reflects how it went - a wipe, or dying early, costs the quest reward -
                // so charging deaths a second time would take it out of the same run twice.
                double mult = 1.0 + bossPart + miniPart + hazardPart + tierPart + playerPart + chestPart;
                long payout = (long)Math.Floor(goldEarned * (double)ModConfig.GoldToTokenRate.Value * mult);

                Log($"end screen: mode={ctx.GameModeName}({ctx.GameModeRaw}) dungeon='{ctx.DungeonName}' " +
                    $"endState={ctx.EndStateName}({ctx.EndStateRaw}) success={success} gold={goldEarned} xp={xpEarned} killsSeen={KillsSeen}");
                Log($"  bosses={BossKills} (+{bossPart:P0})  minis={MiniKills} (+{miniPart:P0})  " +
                    $"tier={ctx.TierRaw} -> {ctx.TierSteps} above first (+{tierPart:P0})");
                Log($"  hazards={ctx.HazardRaw} (+{hazardPart:P0}){ctx.HazardNames}");
                Log($"  players={ctx.Players} (+{playerPart:P0})  chests={ctx.Chests} (+{chestPart:P0})");
                // Recorded for tuning, never charged: see the note above the multiplier.
                Log($"  deaths={ctx.Deaths} (own {ctx.OwnDeaths}, party {ctx.RoomDeaths})  [reported only]");
                Log($"  revives={ctx.Revives} (own {ctx.OwnRevives}, party {ctx.RoomRevives})  [reported only]");
                Log($"  mult={mult:0.###} x {goldEarned} gold -> {payout} tokens");

                // Printed every run: deaths and downs are the best clue to how hard a run actually
                // was, which is what the bonuses get tuned against.
                foreach (PartyMember pm in party)
                    Log($"  player '{pm.Name}' (vid {pm.ViewID}): deaths={pm.Deaths} revives={pm.Revives} " +
                        $"kills={pm.Kills} chests={pm.Chests} coins={pm.Coins}");
                double durationSec = _runStartedUtc == default(DateTime) ? 0.0 : (DateTime.UtcNow - _runStartedUtc).TotalSeconds;

                if (payout <= 0)
                {
                    if (goldEarned <= 0) Log("no gold earned this run — nothing to convert");
                    WriteRunRecord(ctx, party, durationSec, success, goldEarned, xpEarned, mult, 0, false);
                    return;
                }

                bool applied = AddTokens(payout);
                if (applied)
                    ToastLong($"+{payout} tokens  ({goldEarned} gold x{mult:0.##})");
                else
                    Core.Log.Warning(
                        $"payout of {payout} tokens was NOT applied — the LootOverhaul bridge is not wired (see earlier warnings)");

                WriteRunRecord(ctx, party, durationSec, success, goldEarned, xpEarned, mult, payout, applied);
            }
            catch (Exception e)
            {
                Core.Log.Error("payout failed: " + e);
            }
        }

        // ------------------------------------------------------------------ run log
        private const string RunLogHeader =
            "timestamp,scene,game_mode,dungeon_name,end_state,duration_s,success,tier_raw,hazard_level,hazards,players," +
            "bosses,minibosses,kills_seen,chests_local,deaths_party,deaths_own,revives_party,revives_own,gold,xp,mult,payout," +
            "applied,tokens_before,tokens_after,tokens_at_run_start," +
            "rate,boss_bonus,miniboss_bonus,hazard_bonus,tier_bonus,player_bonus,chest_bonus," +
            "party";

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            return "\"" + s.Replace('"', '\'') + "\"";
        }

        private static string PartyText(List<PartyMember> party)
        {
            var parts = new List<string>();
            foreach (PartyMember m in party)
            {
                parts.Add(string.Format("{0}[vid={1} coins={2} questGold={3} lootedGold={4} deaths={5} revives={6} chests={7} kills={8}]",
                    m.Name.Replace('"', '\''), m.ViewID, m.Coins, m.QuestGold, m.LootedGold, m.Deaths, m.Revives, m.Chests, m.Kills));
            }
            return string.Join("; ", parts);
        }

        /// <summary>
        /// Appends one machine-readable row per run to UserData/LootOverhaul/runs.csv, so the tuning
        /// numbers can be reviewed across many sessions. Never throws into the payout path.
        /// </summary>
        private static void WriteRunRecord(RunContext ctx, List<PartyMember> party, double durationSec,
                                           bool success, int gold, int xp, double mult, long payout,
                                           bool applied)
        {
            // Opt-in via [LootOverhaul_Dev] RunLogging (default off): data collection only.
            if (!ModConfig.RunLogging.Value) return;
            try
            {
                string line = string.Join(",", new string[]
                {
                    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                    Csv(_runScene),
                    Csv(ctx.GameModeName),
                    Csv(ctx.DungeonName),
                    Csv(ctx.EndStateName),
                    durationSec.ToString("0"),
                    success ? "1" : "0",
                    ctx.TierRaw.ToString(),
                    ctx.HazardRaw.ToString(),
                    Csv(ctx.HazardNames.Trim(' ', '(', ')')),
                    ctx.Players.ToString(),
                    BossKills.ToString(),
                    MiniKills.ToString(),
                    KillsSeen.ToString(),
                    ctx.Chests.ToString(),
                    ctx.RoomDeaths.ToString(),
                    ctx.OwnDeaths.ToString(),
                    ctx.RoomRevives.ToString(),
                    ctx.OwnRevives.ToString(),
                    gold.ToString(),
                    xp.ToString(),
                    mult.ToString("0.####"),
                    payout.ToString(),
                    applied ? "1" : "0",
                    _tokensBefore.ToString(),
                    _tokensAfter.ToString(),
                    _tokensAtRunStart.ToString(),
                    ModConfig.GoldToTokenRate.Value.ToString("0.###"),
                    ModConfig.BossKillBonus.Value.ToString("0.###"),
                    ModConfig.MinibossKillBonus.Value.ToString("0.###"),
                    ModConfig.HazardBonusPerLevel.Value.ToString("0.###"),
                    ModConfig.TierBonusPerLevel.Value.ToString("0.###"),
                    ModConfig.TokenPlayerBonusPerExtraPlayer.Value.ToString("0.###"),
                    ModConfig.TokenChestBonusPerChest.Value.ToString("0.###"),
                    Csv(PartyText(party)),
                });

                string dir = ModPaths.Root;          // UserData/LootOverhaul, next to the inventory
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "runs.csv");

                // The column set changes as the log grows. If the existing file was written by an
                // older header, keep it by renaming rather than appending misaligned rows.
                if (File.Exists(file))
                {
                    string first = null;
                    using (var reader = new StreamReader(file))
                    {
                        first = reader.ReadLine();
                    }
                    if (first != null && first.Trim().TrimStart((char)0xFEFF) != RunLogHeader)
                    {
                        string archived = Path.Combine(dir, $"runs-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
                        File.Move(file, archived);
                        Core.Log.Msg($"run log format changed — previous rows kept as {Path.GetFileName(archived)}");
                    }
                }
                if (!File.Exists(file))
                    File.WriteAllText(file, RunLogHeader + Environment.NewLine);
                File.AppendAllText(file, line + Environment.NewLine);

                Core.Log.Msg(
                    $"RUN LOGGED — {ctx.GameModeName}, {durationSec / 60.0:0.0} min, {ctx.EndStateName}, " +
                    $"tier {ctx.TierRaw}, haz {ctx.HazardRaw}, {ctx.Players} player(s), " +
                    $"{BossKills} boss / {MiniKills} mini-boss, {ctx.Chests} chests, " +
                    $"{ctx.RoomDeaths} deaths, {ctx.RoomRevives} revives, mult {mult:0.###} => +{payout} tokens");
                Core.Log.Msg($"  -> {file}");
            }
            catch (Exception e)
            {
                Core.Log.Warning("run log failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ bag access
        // Direct: this is LootOverhaul's own code now, so there is nothing to bridge.
        // LootInventory.Gold is a plain long and Inventory loads itself on first touch.

        private static bool TryReadTokens(out long gold)
        {
            try { gold = BagManager.Inventory.Gold; return true; }
            catch (Exception e) { Core.Log.Warning("Tokens: reading the balance failed: " + e.Message); gold = -1L; return false; }
        }

        private static bool AddTokens(long amount)
        {
            try
            {
                var inv = BagManager.Inventory;
                var before = inv.Gold;
                inv.Gold = before + amount;
                inv.Save();
                _tokensBefore = before;
                _tokensAfter = before + amount;
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Error("Tokens: crediting the balance failed: " + e);
                return false;
            }
        }

        private static void Toast(string text) => BagManager.Toast(text);

        /// <summary>
        /// Shows a toast for TokenToastSeconds. The game's notifications have a short fixed life and
        /// ignore their own display-time field - raising it was tried and changed nothing - so the
        /// message is reposted on a timer until the window is over. Only this message is affected.
        /// </summary>
        private static void ToastLong(string text)
        {
            Toast(text);
            float total = ModConfig.Fixed.TokenToastSeconds;
            float every = ModConfig.Fixed.TokenToastRepeatSeconds;
            if (total <= 0f || every <= 0f) return;
            _toastText = text;
            _toastUntil = UnityEngine.Time.unscaledTime + total;
            _toastNextAt = UnityEngine.Time.unscaledTime + every;
        }

        private static void RepeatToast()
        {
            if (_toastText == null) return;
            try
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now >= _toastUntil) { _toastText = null; return; }
                if (now < _toastNextAt) return;
                float every = ModConfig.Fixed.TokenToastRepeatSeconds;
                _toastNextAt = now + (every < 0.5f ? 0.5f : every);
                Toast(_toastText);
            }
            catch { _toastText = null; }
        }

        private sealed class PartyMember
        {
            internal string Name = "?";
            internal int ViewID;
            internal int QuestGold;
            internal int LootedGold;
            internal int Coins;
            internal int Deaths;
            internal int Revives;
            internal int Chests;
            internal int Kills;
        }

        /// <summary>
        /// Snapshot of every player in the room, read from the game's own stats table
        /// (<c>GameManager.playerStatsLUT</c>). Empty when that table is unreadable, so callers fall
        /// back to the local player's own figures.
        /// </summary>
        private static List<PartyMember> ReadParty()
        {
            var list = new List<PartyMember>();
            try
            {
                Il2Cpp.GameManager gm = Il2CppRootMotion.Singleton<Il2Cpp.GameManager>.Instance;
                if (gm == null)
                {
                    Log("party snapshot: no GameManager instance");
                    return list;
                }
                Il2CppSystem.Collections.Generic.Dictionary<Il2Cpp.AvatarPlayer, Il2Cpp.GameManager.PlayerStatDef> lut = gm.playerStatsLUT;
                if (lut == null)
                {
                    Log("party snapshot: playerStatsLUT is null");
                    return list;
                }
                var e = lut.GetEnumerator();
                while (e.MoveNext())
                {
                    Il2Cpp.GameManager.PlayerStatDef s = e.Current.Value;
                    if (s == null) continue;
                    var m = new PartyMember();
                    try { m.Name = ((UnityEngine.Object)(object)e.Current.Key).name; } catch { }
                    try { m.ViewID = s.viewID; } catch { }
                    try { m.QuestGold = s.questGold.Value; } catch { }
                    try { m.LootedGold = s.lootedGold.Value; } catch { }
                    try { m.Coins = Math.Max(0, s.coins.Value); } catch { }
                    try { m.Deaths = Math.Max(0, s.deaths.Value); } catch { }
                    // The game counts a downed-and-revived player separately from a death: in solo
                    // the last stand self-revive lands here and never touches `deaths` at all.
                    try { m.Revives = Math.Max(0, s.revives.Value); } catch { }
                    try { m.Chests = Math.Max(0, s.chests.Value); } catch { }
                    try { m.Kills = Math.Max(0, s.kills.Value); } catch { }
                    list.Add(m);
                }
            }
            catch (Exception ex)
            {
                Core.Log.Warning("party snapshot failed: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// The party's total deaths, or -1 when the table could not be read so callers can fall
        /// back to the local player's own count.
        /// </summary>
        private static int SumPartyDeaths(List<PartyMember> party)
        {
            if (party.Count == 0) return -1;      // unreadable: caller falls back to the local figure
            int total = 0;
            foreach (PartyMember m in party) total += m.Deaths;
            return total;
        }

        /// <summary>
        /// The party's total last stands, or -1 when the table could not be read. Same shape as
        /// <see cref="SumPartyDeaths"/>. Counted separately because the game keeps them apart, and a
        /// real death and a self-revive need not be worth the same.
        /// </summary>
        private static int SumPartyRevives(List<PartyMember> party)
        {
            if (party.Count == 0) return -1;
            int total = 0;
            foreach (PartyMember m in party) total += m.Revives;
            return total;
        }

        /// <summary>
        /// Everything the end screen tells us about the run itself. Each signal is read defensively:
        /// if the game does not expose it, that one bonus is simply worth 0 for the run.
        /// </summary>
        private struct RunContext
        {
            internal int TierRaw;        // TierOverride as int, -1 when the game says "default"
            internal int TierSteps;      // tiers above the first (the end screen shows enum + 1)
            internal int HazardRaw;      // GameManager.HazardLevel as int, 0-3
            internal int HazardSteps;
            internal int GameModeRaw;    // GameMode: 100 DungeonRaid, 200 CrystalHunt, 300 SoulHarvest, 400 Challenge, 600 TombExtraction
            internal string GameModeName;
            internal string DungeonName;
            internal int EndStateRaw;    // MissionEndState: 1 Success, 2 Failure, 3 Forfeit
            internal string EndStateName;
            internal int Players;
            internal int ExtraPlayers;
            internal int Chests;
            internal int Deaths;
            internal int OwnDeaths;
            internal int RoomDeaths;
            internal int Revives;
            internal int OwnRevives;
            internal int RoomRevives;
            internal string HazardNames;

            internal static RunContext Read(Il2Cpp.GameManager.PlayerStatDef psd, List<PartyMember> party)
            {
                var c = new RunContext
                {
                    HazardNames = string.Empty,
                    GameModeName = "?",
                    DungeonName = "",
                    EndStateName = "?",
                };

                try
                {
                    int t = (int)Il2Cpp.UIEndMission.CurrentTier;
                    c.TierRaw = t;
                    c.TierSteps = t > 0 ? t : 0;
                    if (t < 0)
                        Core.Log.Warning("end screen reports Tier_Default - no tier bonus this run");
                }
                catch (Exception e)
                {
                    c.TierRaw = -1;
                    Core.Log.Warning("tier unavailable: " + e.Message);
                }

                try
                {
                    Il2Cpp.DungeonScanner.Dungeon d = Il2Cpp.UIEndMission.CurrentDungeon;
                    if (d != null)
                    {
                        c.HazardRaw = (int)d.hazardLevel;
                        c.HazardSteps = Math.Max(0, c.HazardRaw);
                        c.HazardNames = DescribeHazards(d);
                        try { c.GameModeRaw = (int)d.gameMode; c.GameModeName = d.gameMode.ToString(); } catch { }
                        try { c.DungeonName = d.name; } catch { }
                    }
                }
                catch (Exception e)
                {
                    Core.Log.Warning("dungeon info unavailable: " + e.Message);
                }

                try
                {
                    Il2Cpp.MissionEndState st = Il2Cpp.GameManager.MissionEndState;
                    c.EndStateRaw = (int)st;
                    c.EndStateName = st.ToString();
                }
                catch (Exception e)
                {
                    Core.Log.Warning("mission end state unavailable: " + e.Message);
                }

                try
                {
                    c.Players = Il2CppPhoton.Pun.PhotonNetwork.CurrentRoom.PlayerCount;
                    c.ExtraPlayers = Math.Max(0, c.Players - 1);
                }
                catch (Exception e)
                {
                    c.Players = 1;
                    Core.Log.Warning("party size unavailable: " + e.Message);
                }

                try
                {
                    c.Chests = Math.Max(0, psd.chests.Value);
                }
                catch (Exception e)
                {
                    Core.Log.Warning("chest count unavailable: " + e.Message);
                }

                try
                {
                    c.OwnDeaths = Math.Max(0, psd.deaths.Value);
                }
                catch (Exception e)
                {
                    Core.Log.Warning("death count unavailable: " + e.Message);
                }

                try
                {
                    c.RoomDeaths = SumPartyDeaths(party);
                }
                catch (Exception e)
                {
                    Core.Log.Warning("party death sum unavailable: " + e.Message);
                }

                try
                {
                    c.OwnRevives = Math.Max(0, psd.revives.Value);
                }
                catch (Exception e)
                {
                    Core.Log.Warning("last stand count unavailable: " + e.Message);
                }

                try
                {
                    c.RoomRevives = SumPartyRevives(party);
                }
                catch (Exception e)
                {
                    Core.Log.Warning("party last stand sum unavailable: " + e.Message);
                }

                // Always the whole party, matching how the game itself aggregates a run.
                // Falls back to the local count only if the game's stats table was unreadable.
                c.Deaths = c.RoomDeaths >= 0 ? c.RoomDeaths : c.OwnDeaths;
                c.Revives = c.RoomRevives >= 0 ? c.RoomRevives : c.OwnRevives;

                return c;
            }

            private static string DescribeHazards(Il2Cpp.DungeonScanner.Dungeon d)
            {
                try
                {
                    Il2CppSystem.Collections.Generic.List<Il2Cpp.GameManager.HazardModifier> list = d.hazards;
                    if (list == null || list.Count == 0) return string.Empty;
                    var names = new List<string>();
                    for (int i = 0; i < list.Count && i < 8; i++)
                        names.Add(list[i].ToString());
                    return "  (" + string.Join(", ", names) + ")";
                }
                catch
                {
                    return string.Empty;
                }
            }
        }


        // ------------------------------------------------------------------ helpers
        internal static void Log(string message)
        {
            if (ModConfig.VerboseLogging.Value) Core.Log.Msg(message);
        }
    }
}
