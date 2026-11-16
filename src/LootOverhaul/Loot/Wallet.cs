using System;
using Il2Cpp;
using LootOverhaul.Recon;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The kobold's money is the game's own gold (0.15.0; the mod's tokens are gone). The balance
    /// is the character's <c>Coins</c> value in the player profile, read with
    /// <c>PlayerProfile.GetCharacterData</c> and changed with <c>IncrementCharacterData</c>, the
    /// call the game itself uses to pay salvage and to charge at the fabricator (a negative count
    /// takes gold away). The profile is saved after every change, like the game does after a sale.
    ///
    /// <see cref="FabricatorBridge"/> refuses gold the game pays out for a bag weapon salvaged at
    /// the armory or trashed at the fabricator; while this class is paying, that check stands aside
    /// (<see cref="Paying"/>), so the kobold's own price for that weapon still arrives.
    /// </summary>
    public static class Wallet
    {
        /// <summary>True while the wallet itself calls <c>IncrementCharacterData</c>.</summary>
        internal static bool Paying;

        private static int _cachedFrame = -1;
        private static long _cached;

        /// <summary>
        /// The character's gold, or 0 when the profile cannot be read. Read from the profile once per
        /// frame: a booth page asks for it in every row, and each read is an Il2Cpp call (and, when it
        /// fails, a warning). Every change made here forgets the cached value.
        /// </summary>
        public static long Balance
        {
            get
            {
                var frame = UnityEngine.Time.frameCount;
                if (frame == _cachedFrame) return _cached;
                _cached = ReadNow();
                _cachedFrame = frame;
                return _cached;
            }
        }

        private static long ReadNow()
        {
            {
                try
                {
                    var o = PlayerProfile.GetCharacterData(PlayerData.CharacterValues.Coins);
                    if (o == null) return 0L;
                    // The value is boxed (an int or a long, depending on how the profile was read);
                    // its text is the same either way.
                    return long.TryParse(o.ToString(), out var v) ? Math.Max(0L, v) : 0L;
                }
                catch (Exception e)
                {
                    Core.Log.Warning($"Wallet: reading gold failed: {e.GetType().Name}: {e.Message}");
                    return 0L;
                }
            }
        }

        public static bool CanAfford(long price) => price <= 0 || Balance >= price;

        /// <summary>
        /// Takes <paramref name="price"/> gold, or toasts what is missing and takes nothing.
        /// <paramref name="what"/> finishes "… costs N gold": "Upgrading", "The Big Bag".
        /// </summary>
        public static bool TrySpend(long price, string what)
        {
            if (price <= 0) return true;
            var have = ReadNow();
            if (have < price)
            {
                BagManager.Toast($"{what} costs {UiKit.Money(price)}; you have {UiKit.Number(have)}.");
                return false;
            }
            return Change(-price, what);
        }

        /// <summary>Pays <paramref name="amount"/> gold to the player (a sale, a refund).</summary>
        public static bool Pay(long amount, string why) => amount <= 0 || Change(amount, why);

        private static bool Change(long delta, string why)
        {
            _cachedFrame = -1;
            try
            {
                var before = ReadNow();
                bool ok;
                Paying = true;
                try { ok = PlayerProfile.IncrementCharacterData(PlayerData.CharacterValues.Coins, (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, delta))); }
                finally { Paying = false; }
                var after = ReadNow();
                _cachedFrame = -1;
                if (!ok && after == before)
                {
                    Core.Log.Warning($"Wallet: the game refused {delta:+#;-#;0} gold ({why}); balance stays {before}.");
                    BagManager.Toast("The game would not change your gold. Nothing happened.");
                    return false;
                }
                try { PlayerProfile.SavePlayerProfile(false, null, null); }
                catch (Exception e) { Core.Log.Warning($"Wallet: saving the profile failed: {e.GetType().Name}: {e.Message}"); }
                ReconLog.Line($"gold {delta:+#;-#;0} ({why}): {before} -> {after}");
                return true;
            }
            catch (Exception e)
            {
                Paying = false;
                Core.Log.Error($"Wallet: changing gold by {delta} failed ({why}): {e}");
                BagManager.Toast("Your gold could not be changed. Nothing happened.");
                return false;
            }
        }
    }
}
