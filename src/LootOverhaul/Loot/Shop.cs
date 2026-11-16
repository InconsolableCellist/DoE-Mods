using System;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// What the kobold sells. Since 0.15.0 it sells no weapons (the game's own vendor does, and
    /// the kobold enchants them), so this is the tonic price and the count for the recon report.
    /// </summary>
    public static class Shop
    {
        public static int Bought;

        /// <summary>
        /// A tonic's asking price in gold: the catalogue price × ShopPriceMultiplier (400 / 1,000 /
        /// 2,000 at the defaults). Tonics are brewed to order for the next run, see <see cref="PreparedTonics"/>.
        /// </summary>
        public static int TonicPrice(Buffs.Def def, int tier) => (int)Math.Round(def.Prices[Math.Max(0, Math.Min(2, tier))] * ModConfig.ShopPriceMultiplier.Value);
    }
}
