using System;
using Descent.Gate;
using Descent.Net;
using Descent.Recon;
using Descent.Run;
using Il2Cpp;
using Il2CppPhoton.Pun;
using Newtonsoft.Json;
using Hashtable = Il2CppExitGames.Client.Photon.Hashtable;

namespace Descent.Dungeon
{
    /// <summary>The run and floor as they travel: the room property and every event payload carry this.</summary>
    public class RunState
    {
        public string Op;
        public RunRecord Run;
        public FloorSpec Floor;
        public float Seconds;
        public string ModVersion = Core.Version;
    }

    /// <summary>
    /// What the party is playing right now, kept in two places: the room's custom property
    /// <c>dd.run</c> (survives a host switch; read by late joiners) and our own memory.
    /// Also the dispatcher for the run event (code 171).
    /// </summary>
    public static class RunSync
    {
        public const string RoomKey = "dd.run";

        /// <summary>The floor being played, or null when no descent is live.</summary>
        public static FloorSpec Floor { get; private set; }
        public static RunRecord Run { get; private set; }
        public static bool Active => Floor != null && Run != null;

        public static event Action<int, RunState> RunEvent;

        public static void Init()
        {
            ModNet.RegisterHandler(ModNet.CodeRun, OnRunEvent);
        }

        public static void Set(RunRecord run, FloorSpec floor, string why)
        {
            Run = run;
            Floor = floor;
            ReconLog.Headline(floor == null ? $"Run state cleared ({why})." : $"Run state: {floor.Describe()} ({why}).");
        }

        public static void Clear(string why) => Set(null, null, why);

        // ---- room property ------------------------------------------------------------------

        /// <summary>Host only. Written before every load so a new host or a late joiner can read it.</summary>
        public static void WriteRoomProperty(RunRecord run, FloorSpec floor)
        {
            try
            {
                if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
                var props = new Hashtable();
                var json = floor == null ? "" : JsonConvert.SerializeObject(new RunState { Op = "state", Run = run, Floor = floor });
                props[(Il2CppSystem.String)RoomKey] = (Il2CppSystem.String)json;
                PhotonNetwork.CurrentRoom.SetCustomProperties(props, null, null);
                ReconLog.Line($"room property {RoomKey} <- {(floor == null ? "(cleared)" : floor.Describe())} ({json.Length} chars)");
            }
            catch (Exception e) { Core.Log.Warning($"Room property write failed: {e.GetType().Name}: {e.Message}"); }
        }

        public static RunState ReadRoomProperty()
        {
            try
            {
                if (!PhotonNetwork.InRoom) return null;
                var props = PhotonNetwork.CurrentRoom.CustomProperties;
                if (ReferenceEquals(props, null)) return null;
                var k = (Il2CppSystem.String)RoomKey;
                if (!props.ContainsKey(k)) return null;
                var v = props[k];
                var json = ReferenceEquals(v, null) ? null : v.ToString();
                if (string.IsNullOrEmpty(json)) return null;
                return JsonConvert.DeserializeObject<RunState>(json);
            }
            catch (Exception e) { Core.Log.Warning($"Room property read failed: {e.GetType().Name}: {e.Message}"); return null; }
        }

        // ---- events ---------------------------------------------------------------------------

        public static bool Send(RunState state, int[] targets = null)
        {
            try
            {
                var json = JsonConvert.SerializeObject(state);
                return ModNet.Send(ModNet.CodeRun, json, reliable: true, targetActors: targets);
            }
            catch (Exception e) { Core.Log.Warning($"Run event send failed: {e.GetType().Name}: {e.Message}"); return false; }
        }

        private static void OnRunEvent(int sender, Il2CppSystem.Object content)
        {
            var json = ModNet.AsString(content);
            if (string.IsNullOrEmpty(json)) return;
            RunState state;
            try { state = JsonConvert.DeserializeObject<RunState>(json); }
            catch (Exception e) { Core.Log.Warning($"Run event from actor {sender} unreadable: {e.GetType().Name}"); return; }
            if (state == null) return;
            ReconLog.Line($"run event `{state.Op}` from actor {sender}: {(state.Floor == null ? "(no floor)" : state.Floor.Describe())}");
            if (state.Run != null) RunStore.Upsert(state.Run, onlyIfNewer: true);
            try { RunEvent?.Invoke(sender, state); }
            catch (Exception e) { Core.Log.Error($"Run event handler threw: {e}"); }
        }

        // ---- scene bookkeeping ----------------------------------------------------------------

        /// <summary>Entering the dungeon scene: the room property says which floor this is.</summary>
        public static void OnDungeonScene()
        {
            var state = ReadRoomProperty();
            if (state?.Floor == null)
            {
                if (Active) ReconLog.Line("dungeon scene: no dd.run property; keeping the floor we launched.");
                else ReconLog.Line("dungeon scene: no dd.run property — a vanilla dungeon.");
                return;
            }
            var run = state.Run != null ? RunStore.Upsert(state.Run, onlyIfNewer: true) : RunStore.Find(state.Floor.RunId);
            Set(run, state.Floor, "room property");
            try
            {
                GameManager.DifficultyTier = (TierOverride)state.Floor.Tier;
                ReconLog.Line($"DifficultyTier set to {GameManager.DifficultyTier} from the room property.");
            }
            catch (Exception e) { Core.Log.Warning($"DifficultyTier set failed: {e.GetType().Name}"); }
        }

        /// <summary>Back in the hub: whatever floor was live is over. The prefix already recorded a descent; this records the rest.</summary>
        public static void OnLobbyScene()
        {
            if (!Active) return;
            var run = Run; var floor = Floor;
            var end = "?";
            try { end = GameManager.MissionEndState.ToString(); } catch { }
            if (!run.Completed)
            {
                run.Touch();
                RunStore.Upsert(run);
                ReconLog.Headline($"Back in the hub from floor {floor.Number} (end state {end}); `{run.Name}` resumes at floor {run.FloorIndex + 1}.");
            }
            else ReconLog.Headline($"Back in the hub; `{run.Name}` is complete.");
            if (PhotonNetwork.IsMasterClient) WriteRoomProperty(null, null);
            Clear("lobby");
        }

        /// <summary>The exit fired on the last floor, or the prefix let a failure through: bookkeeping only.</summary>
        public static void MarkCompleted()
        {
            if (!Active) return;
            Run.Completed = true;
            Run.DeepestFloor = Math.Max(Run.DeepestFloor, Floor.Index);
            Run.Touch();
            RunStore.Upsert(Run);
            ReconLog.Headline($"*** `{Run.Name}` COMPLETED on floor {Floor.Number}.");
        }

        public static void MarkInterrupted(string how)
        {
            if (!Active) return;
            Run.DeepestFloor = Math.Max(Run.DeepestFloor, Floor.Index);
            Run.Touch();
            RunStore.Upsert(Run);
            ReconLog.Headline($"`{Run.Name}` interrupted on floor {Floor.Number} ({how}); resumes there.");
        }

        /// <summary>Host: a peer joined mid-run — hand them the state so their copy can host later.</summary>
        public static void OnPeerJoined(ModPeer peer)
        {
            if (!Active || peer.IsLocal || !PhotonNetwork.IsMasterClient) return;
            Send(new RunState { Op = "state", Run = Run, Floor = Floor }, new[] { peer.ActorNumber });
        }
    }
}
