using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The bag and the three battle slots, persisted as JSON under
    /// <c>UserData/LootOverhaul/inventory/&lt;account&gt;.json</c>. This is the *only* place
    /// mod ownership lives: nothing here ever reaches PlayFab (docs/LOOT-OVERHAUL.md, design
    /// decision 1).
    ///
    /// Written on every change rather than on quit — a crash mid-run must not lose a legendary.
    /// </summary>
    public class LootInventory
    {
        /// <summary>Schema version for future migrations.</summary>
        public int Version = 1;

        public List<LootItem> Items = new List<LootItem>();

        /// <summary>Mod currency, earned only by selling at the booth.</summary>
        public long Gold;

        /// <summary>
        /// Battle loadout, one entry per stock slot (left hip, right hip, back), in the game's
        /// <c>Holster.SaveSlot</c> order. Null means "whatever the vanilla loadout says".
        /// A value is either a <see cref="LootItem.Id"/> or a vanilla armory weapon's save
        /// string prefixed with <c>vanilla:</c> — the booth picker offers both sources.
        /// </summary>
        public string[] Slots = new string[3];

        /// <summary>Legendary pity counter: kills since the last legendary drop for this account.</summary>
        public int KillsSinceLegendary;

        [JsonIgnore] public string Path { get; private set; }
        [JsonIgnore] public float TotalWeight { get { var w = 0f; foreach (var i in Items) w += i.Weight; return w; } }

        public static LootInventory Load(string path)
        {
            LootInventory inv = null;
            try
            {
                if (File.Exists(path))
                    inv = JsonConvert.DeserializeObject<LootInventory>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                // Never overwrite a file we failed to parse: move it aside so the loot survives
                // for a hand repair, then start fresh.
                Core.Log.Error($"Inventory at {path} is unreadable ({e.GetType().Name}: {e.Message}); moving it aside.");
                try { File.Move(path, path + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}"); } catch { }
            }

            inv ??= new LootInventory();
            inv.Path = path;
            if (inv.Slots == null || inv.Slots.Length != 3) inv.Slots = new string[3];
            inv.Items ??= new List<LootItem>();
            return inv;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(Path)) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                var tmp = Path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
                File.Move(tmp, Path, overwrite: true);
            }
            catch (Exception e)
            {
                Core.Log.Error($"Inventory save failed: {e.GetType().Name}: {e.Message}");
            }
        }

        public bool CanCarry(float extraWeight, float capacity) => TotalWeight + extraWeight <= capacity;

        public LootItem Find(string id) => Items.Find(i => i.Id == id);

        public bool Remove(string id)
        {
            var idx = Items.FindIndex(i => i.Id == id);
            if (idx < 0) return false;
            Items.RemoveAt(idx);
            for (var s = 0; s < Slots.Length; s++)
                if (Slots[s] == id) Slots[s] = null;
            return true;
        }
    }
}
