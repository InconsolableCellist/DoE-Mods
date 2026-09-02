using System.Collections.Generic;
using System.Linq;
using LootOverhaul.Net;

namespace LootOverhaul.Recon
{
    /// <summary>
    /// Counts every inbound Photon event code below 200 (PUN reserves 200+). Tells us whether
    /// anything the game — or CustomAvatars, when both mods run — puts on the wire lands in
    /// our 150–159 block. Dumped on every scene change and at quit.
    /// </summary>
    public static class EventTally
    {
        private static readonly Dictionary<byte, int> Counts = new Dictionary<byte, int>();
        private static readonly object Gate = new object();

        public static void Install()
        {
            PhotonHook.RawEvent += (code, sender, content) =>
            {
                if (code >= 200) return;
                lock (Gate)
                {
                    Counts.TryGetValue(code, out var n);
                    Counts[code] = n + 1;
                }
            };
        }

        public static void Report(string when)
        {
            ReconLog.Section($"Photon event codes < 200 seen so far ({when})");
            lock (Gate)
            {
                if (Counts.Count == 0) { ReconLog.Line("- none"); return; }
                var inBlock = 0;
                foreach (var kv in Counts.OrderBy(k => k.Key))
                {
                    var note = kv.Key >= ModNet.CodeMin && kv.Key <= ModNet.CodeMax ? "  <-- IN OUR 150–159 BLOCK"
                             : kv.Key >= 140 && kv.Key <= 149 ? "  (CustomAvatars block)" : "";
                    if (note.StartsWith("  <--")) inBlock++;
                    ReconLog.Line($"- code {kv.Key}: ×{kv.Value}{note}");
                }
                ReconLog.Headline(inBlock == 0
                    ? "Event block 150–159: clear so far."
                    : $"Event block 150–159: {inBlock} foreign code(s) seen — pick another block.");
            }
        }
    }
}
