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
        /// An entry is a copy of the weapon record (DTO fields), from either the bag or the
        /// vanilla armory; <see cref="LootItem.Source"/> says which. Bag items in use carry
        /// <see cref="LootItem.EquippedSlot"/> so they cannot be sold or dropped meanwhile.
        /// </summary>
        public LootItem[] Loadout = new LootItem[3];

        /// <summary>Bag upgrades bought at the kobold: an index into <see cref="BagManager.BagUpgrades"/> (0 = none).</summary>
        public int BagLevel;

        /// <summary>Legendary pity counter: kills since the last legendary drop for this account.</summary>
        public int KillsSinceLegendary;

        /// <summary>
        /// Weapon GUIDs of loot that has left the bag (sold, dropped, trashed, enchanted into a
        /// new one). The game's armory keeps the module objects it was handed until the
        /// profile next loads (<c>AvatarCustomizer.InitWeaponModules</c> runs only from
        /// <c>OnPlayerProfileLoaded</c>), so a sold weapon still sits on its pedestal looking
        /// vanilla; these GUIDs keep it from ever being salvaged for coins or unlocked for real.
        /// </summary>
        public List<string> RetiredGuids = new List<string>();

        /// <summary>The broker's current stock for this player (Value holds the asking price) and when it was rolled.</summary>
        public List<LootItem> ShopStock = new List<LootItem>();
        public DateTime ShopGeneratedAt = DateTime.MinValue;

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
            if (inv.Loadout == null || inv.Loadout.Length != 3) inv.Loadout = new LootItem[3];
            inv.Items ??= new List<LootItem>();
            inv.ShopStock ??= new List<LootItem>();
            inv.RetiredGuids ??= new List<string>();
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

        /// <summary>
        /// The battle slot this item is in, or -1, from the loadout table, which is what the
        /// holster fill reads. The item's own <see cref="LootItem.EquippedSlot"/> is a display
        /// hint kept in step by <see cref="Loadout.Set"/>; every sell, sweep, drop and toss asks
        /// here instead, so the two can never disagree about what may leave the bag (0.9.15).
        /// </summary>
        public int EquippedSlotOf(LootItem item)
        {
            if (item == null) return -1;
            for (var s = 0; s < Loadout.Length; s++)
                if (Loadout[s] != null && Loadout[s].Id == item.Id) return s;
            return -1;
        }

        /// <summary>Equipped in a battle slot (weapons) or worn (armor): never sold, swept, dropped or tossed.</summary>
        public bool InUse(LootItem item) => item != null && (EquippedSlotOf(item) >= 0 || item.WornSlot >= 0);

        public bool Remove(string id)
        {
            var idx = Items.FindIndex(i => i.Id == id);
            if (idx < 0) return false;
            var gone = Items[idx];
            Items.RemoveAt(idx);
            if (gone.IsWeapon && !string.IsNullOrEmpty(gone.WeaponGuid))
            {
                var g = FabricatorBridge.Norm(gone.WeaponGuid);
                if (!RetiredGuids.Contains(g)) { RetiredGuids.Add(g); if (RetiredGuids.Count > 500) RetiredGuids.RemoveAt(0); }
                try { FabricatorBridge.OnBagWeaponGone(gone); } catch { }
            }
            for (var s = 0; s < Loadout.Length; s++)
                if (Loadout[s] != null && Loadout[s].Id == id) Loadout[s] = null;
            return true;
        }
    }
}
