using UnityEngine;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The one look of the kobold stall and the bag (0.13.0): four text sizes, one colour per
    /// meaning, one panel grid. Every panel takes its sizes, colours and positions from here;
    /// none sets its own. Read in a headset, so nothing is drawn below <see cref="Floor"/>.
    /// </summary>
    public static class UiStyle
    {
        // ---- type: TextMeshPro sizes (1 ≈ 0.1 m glyphs). Em height at the stall's 1.1 scale in brackets.
        public const float Sign = 0.9f;      // KOBOLD TRAVELER (9.9 cm)
        public const float Title = 0.56f;    // panel headers, card titles (6.2 cm)
        public const float Row = 0.46f;      // item names, prices, values, the token balance (5.1 cm)
        public const float Label = 0.42f;    // every button label, bold (4.6 cm)
        public const float Body = 0.36f;     // stats, help, captions, status lines (4.0 cm)
        public const float Floor = 0.30f;    // nothing renders smaller, after auto-fit (3.3 cm)
        /// <summary>Auto-fit may shrink a label to this share of its size, but never below <see cref="Floor"/>.</summary>
        public const float FitShare = 0.8f;

        // ---- colours, one meaning each. Hex strings are for rich text, Colors for bars.
        public const string TextHex = "#F2EFE6";       // names, values, labels
        public const string Text2Hex = "#A3ACBA";      // stats, help, captions, context (the one grey)
        public const string GoldHex = "#F5C542";       // tokens and prices only
        public const string GreenHex = "#5BD75B";      // better, next, on, prepared, maxed
        public const string RedHex = "#F06A6A";        // can't afford, worse, off
        public const string TonicHex = "#7FD8FF";      // tonic names and effects
        public static readonly Color Text = Hex(TextHex);
        public static readonly Color Text2 = Hex(Text2Hex);
        public static readonly Color Gold = Hex(GoldHex);
        public static readonly Color Green = Hex(GreenHex);
        public static readonly Color Red = Hex(RedHex);
        public static readonly Color Accent = Hex("#4D8CFF");      // card top edge
        public static readonly Color Panel = Hex("#0F141C");       // the one backdrop
        public static readonly Color Stripe = Hex("#1A2230");      // every other row
        public static readonly Color StripeOk = Hex("#163020");    // a prepared tonic's row
        public static readonly Color Card = Hex("#161D29");
        public static readonly Color Rule = Hex("#33415A");
        public static readonly Color Track = Hex("#2A3446");       // progress bar background
        public static readonly Color Fill = Hex("#C9D3E3");        // progress bar so far

        /// <summary>Rarity colours for armor names (Rare and Legendary lifted for contrast on the dark panel).</summary>
        public static readonly string[] ArmorHex = { "#4EA2FF", "#3FCF3D", "#7A86FF", "#B48CFF" };
        /// <summary>Junk tiers: plain, fine, valuable (orange, because gold means tokens).</summary>
        public static readonly string[] JunkHex = { "#9A9A9A", "#E8E8E8", "#FF9F43" };

        public enum Variant { Primary, Secondary, Danger, Sell, Tab }
        public enum State { Normal, Disabled, Selected }

        /// <summary>Plate, frame, label and hover plate of a button.</summary>
        public struct Look { public Color Plate, Frame, Label, Hover; }

        public static Look ButtonLook(Variant v, State s)
        {
            if (s == State.Disabled) return L("#141922", "#2C3442", "#6B7482", "#141922");
            if (s == State.Selected) return L("#EEF1F6", "#EEF1F6", "#0F141C", "#FFFFFF");
            switch (v)
            {
                case Variant.Secondary: return L("#1D2533", "#6B7A94", "#E4E8EF", "#2E394C");
                case Variant.Danger: return L("#3A1719", "#E06060", "#FFE3E3", "#5E2427");
                case Variant.Sell: return L("#143C20", "#5BD75B", "#FFFFFF", "#1F5A30");
                case Variant.Tab: return L("#151C27", "#3A4A66", "#DCE2EC", "#25324A");
                default: return L("#15305E", "#4D8CFF", "#FFFFFF", "#23508F");
            }
        }
        private static Look L(string p, string f, string l, string h) => new Look { Plate = Hex(p), Frame = Hex(f), Label = Hex(l), Hover = Hex(h) };

        // ---- buttons (metres)
        public static readonly Vector2 Action = new Vector2(0.20f, 0.08f);
        public static readonly Vector2 Compact = new Vector2(0.14f, 0.07f);
        public static readonly Vector2 Stepper = new Vector2(0.095f, 0.08f);
        public const float TabHeight = 0.075f;
        public const float Gap = 0.02f;            // between any two hit boxes
        public const float FrameWidth = 0.006f;

        // ---- panel grid (metres, from the panel's centre; every panel and the bag)
        public const float PanelWidth = 1.45f;
        public const float PanelHeight = 1.26f;
        public const float Pad = 0.04f;
        public const float Top = PanelHeight * 0.5f;
        public const float Left = -PanelWidth * 0.5f + Pad;
        public const float Right = PanelWidth * 0.5f - Pad;
        public const float HeaderY = Top - 0.07f;
        public const float StripY = Top - 0.17f;
        public const float ToolY = Top - 0.265f;
        public const float Row0Y = Top - 0.38f;
        public const float RuleHeaderY = Top - 0.12f;
        public const float RuleStripY = Top - 0.215f;
        public const float RuleToolY = Top - 0.31f;
        public const float PagerY = -PanelHeight * 0.5f + 0.055f;
        public const float Pitch = 0.14f;          // list rows
        public const float Compact_Pitch = 0.105f; // help, compare and ring rows
        public const float IconX = Left + 0.04f;
        public const float TagX = Left + 0.18f;
        public const float TagWidth = 0.20f;
        public const float TextX = Left + 0.29f;
        public const float PriceWidth = 0.16f;
        public const float CoinSize = 0.03f;

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
    }
}
