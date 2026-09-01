using UnityEngine;
using DoEFriendsMod.Avatars;
using DoEFriendsMod.Gate;

namespace DoEFriendsMod
{
    /// <summary>
    /// A small always-there panel on the desktop window: what the mod is doing right now, and
    /// which key does what.
    ///
    /// Deliberately not interactive and deliberately not in VR. Unity's IMGUI draws to the
    /// desktop mirror, not into the headset, so buttons here would be no easier to reach than
    /// the keys they'd replace — you'd still have to take the headset off. What it is good for
    /// is the thing that actually costs time: knowing, at a glance and without reading the log,
    /// whether the gate is open and which avatar is selected.
    /// </summary>
    public static class Overlay
    {
        public static bool Visible = true;

        private const int Width = 330;
        private const int Pad = 10;

        public static void Draw()
        {
            if (!Visible) return;

            var lines = BuildLines(out var title);
            var height = Pad * 2 + 20 + lines.Length * 16;
            var rect = new Rect(Pad, Pad, Width, height);

            GUI.Box(rect, title);

            var y = rect.y + 24;
            foreach (var line in lines)
            {
                GUI.Label(new Rect(rect.x + Pad, y, Width - Pad * 2, 16), line);
                y += 16;
            }
        }

        private static string[] BuildLines(out string title)
        {
            title = $"DoEFriendsMod {Core.Version}";

            var gate = ModGate.Active ? "ACTIVE" : "inert";
            var reason = ModGate.Reason ?? "";
            if (reason.Length > 44) reason = reason.Substring(0, 44) + "…";

            var selected = ModConfig.PreviewAvatarName.Value;
            if (string.IsNullOrWhiteSpace(selected)) selected = "(first available)";

            var worn = Core.Instance?.SwapSummary ?? "-";

            return new[]
            {
                $"Gate: {gate} — {reason}",
                $"Avatar: {selected}",
                $"Swapped: {worn}",
                "",
                "F1  hide this panel        F2  next avatar",
                "F3  reload settings        F4  wear avatar",
                "F5  rescan avatars         F6  preview",
                "F7  dump environment       F8  dump avatars",
                "F9  dump room + events",
                "",
                "Settings: UserData/MelonPreferences.cfg",
                "then press F3. Window must be focused for keys.",
            };
        }
    }
}
