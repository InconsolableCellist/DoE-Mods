using System;
using System.Collections;
using Descent.Gate;
using Descent.Recon;
using Descent.Run;
using Il2Cpp;
using Il2CppPhoton.Pun;
using MelonLoader;
using UnityEngine;

namespace Descent.Dungeon
{
    /// <summary>
    /// The floor hand-off. The exit teleporter ends a vanilla mission with
    /// <c>MissionSuccess</c> (lock the room, end state Success, music) and then
    /// <c>GameManager.ReturnToLobby</c>, which on the host raises the game's ReturnToLobby
    /// event so every client runs the lobby transition. The prefix here catches that call
    /// while a descent is live and, unless this was the last floor, replaces the transition
    /// with: bank the floor, advance the run, load the next floor. Deaths and forfeits
    /// (end state Failure / Forfeit) go through untouched, with the run left at this floor.
    /// </summary>
    public static class Descender
    {
        public static bool InProgress { get; private set; }
        public static int Descents { get; private set; }
        public static int Suppressed { get; private set; }

        public static void Install()
        {
            Hooks.Patch(typeof(GameManager), "ReturnToLobby",
                Hooks.Of(typeof(Descender), nameof(ReturnToLobby_Prefix)), null, paramCount: 1);
            Hooks.Patch(typeof(GameManager), "MissionSuccess",
                null, Hooks.Of(typeof(Descender), nameof(MissionSuccess_Postfix)), paramCount: 0);
        }

        private static void MissionSuccess_Postfix()
        {
            try
            {
                ReconLog.Line($"MissionSuccess: run {(RunSync.Active ? RunSync.Floor.Describe() : "(none)")}, end state {GameManager.MissionEndState}, host {PhotonNetwork.IsMasterClient}");
            }
            catch { }
        }

        private static bool ReturnToLobby_Prefix(bool sendAnalytics)
        {
            try
            {
                if (!ModConfig.Enabled.Value || ModConfig.ObserveOnly.Value || !RunSync.Active) return true;
                if (!ModGate.Active)
                {
                    ReconLog.Headline($"ReturnToLobby with a live floor but the gate is inert ({ModGate.Reason}); vanilla return.");
                    RunSync.MarkInterrupted("gate inert");
                    return true;
                }
                MissionEndState end;
                try { end = GameManager.MissionEndState; }
                catch { return true; }
                var floor = RunSync.Floor;
                ReconLog.Line($"ReturnToLobby(sendAnalytics={sendAnalytics}) on floor {floor.Number}: end state {end}, host {PhotonNetwork.IsMasterClient}, in progress {InProgress}");

                if (end != MissionEndState.Success)
                {
                    RunSync.MarkInterrupted(end.ToString());
                    return true;
                }
                if (floor.IsLast)
                {
                    RunSync.MarkCompleted();
                    if (PhotonNetwork.IsMasterClient) RunSync.WriteRoomProperty(null, null);
                    return true;
                }
                if (InProgress) { Suppressed++; return false; }
                if (!PhotonNetwork.IsMasterClient)
                {
                    // The host's call is the one that matters; ours would only have returned early anyway.
                    Suppressed++;
                    ReconLog.Line("non-host ReturnToLobby suppressed; the host's descend event follows.");
                    return false;
                }
                Begin(RunSync.Run, floor, "exit teleporter");
                return false;
            }
            catch (Exception e)
            {
                Core.Log.Error($"ReturnToLobby prefix threw ({e.GetType().Name}: {e.Message}); vanilla return.");
                return true;
            }
        }

        /// <summary>Host: bank, advance, tell peers, load floor N+1.</summary>
        public static void Begin(RunRecord run, FloorSpec floor, string why)
        {
            if (InProgress) return;
            InProgress = true;
            MelonCoroutines.Start(HostRoutine(run, floor, why));
        }

        private static IEnumerator HostRoutine(RunRecord run, FloorSpec floor, string why)
        {
            ReconLog.Headline($"*** Descending from floor {floor.Number} ({why}).");
            try { NetManager.LockRoom(); } catch { }
            try { UIQuickSettings.Hide(); } catch { }
            var delay = Mathf.Max(0.5f, ModConfig.DescendDelaySeconds.Value);
            yield return new WaitForSeconds(delay * 0.4f);

            var banked = false;
            if (ModConfig.BankRewardsPerFloor.Value)
            {
                try { banked = Rewards.Bank(floor); }
                catch (Exception e) { Core.Log.Error($"Banking threw: {e.GetType().Name}: {e.Message}"); }
            }

            run.FloorIndex = floor.Index + 1;
            run.DeepestFloor = Math.Max(run.DeepestFloor, floor.Index);
            if (banked) run.BankedThrough = Math.Max(run.BankedThrough, floor.Index);
            run.Touch();
            RunStore.Upsert(run);
            var next = run.Floor(run.FloorIndex);
            Descents++;
            RunSync.Set(run, next, "descend");
            RunSync.WriteRoomProperty(run, next);
            RunSync.Send(new RunState { Op = "descend", Run = run, Floor = next });
            Rewards.Toast($"<b>Descending</b> to floor {next.Number}: {next.RealmName}, tier {next.Tier + 1}{(next.Boss ? " — something waits below" : "")}");

            yield return new WaitForSeconds(delay * 0.6f);
            try { Launcher.Execute(next, "descend"); }
            catch (Exception e) { Core.Log.Error($"Descend launch threw: {e.GetType().Name}: {e.Message}"); }
            InProgress = false;
        }

        /// <summary>Peer: the host is taking us down. Bank our own floor, then run the launch coroutine (no LoadLevel on a non-host).</summary>
        public static void OnPeerDescend(int sender, RunState state)
        {
            if (InProgress) return;
            InProgress = true;
            MelonCoroutines.Start(PeerRoutine(sender, state));
        }

        private static IEnumerator PeerRoutine(int sender, RunState state)
        {
            var finished = RunSync.Floor;
            ReconLog.Headline($"*** Host (actor {sender}) is descending to floor {state.Floor.Number}.");
            var delay = Mathf.Max(0.5f, ModConfig.DescendDelaySeconds.Value);
            if (ModConfig.BankRewardsPerFloor.Value && finished != null)
            {
                MissionEndState end = MissionEndState.Failure;
                try { end = GameManager.MissionEndState; } catch { }
                if (end == MissionEndState.Success)
                {
                    try { Rewards.Bank(finished); }
                    catch (Exception e) { Core.Log.Error($"Banking threw: {e.GetType().Name}: {e.Message}"); }
                }
                else ReconLog.Line($"peer banking skipped: end state here is {end} (MissionSuccess has not run on this client).");
            }
            var run = RunStore.Upsert(state.Run, onlyIfNewer: true);
            RunSync.Set(run, state.Floor, $"descend from actor {sender}");
            yield return new WaitForSeconds(delay * 0.3f);
            try { Launcher.Execute(state.Floor, "descend (peer)"); }
            catch (Exception e) { Core.Log.Error($"Descend launch threw: {e.GetType().Name}: {e.Message}"); }
            InProgress = false;
        }

        /// <summary>Scene changed: whatever was in flight is over.</summary>
        public static void Reset() => InProgress = false;

        // ---- dev hotkeys --------------------------------------------------------------------------

        /// <summary>Backspace in a dungeon: what the exit teleporter does, without walking to it.</summary>
        public static void ForceDescend()
        {
            if (!RunSync.Active) { Rewards.Toast("No descent is live."); return; }
            if (!PhotonNetwork.IsMasterClient) { Rewards.Toast("Only the host can force a descent."); return; }
            ReconLog.Headline("Hotkey: forcing the exit (MissionSuccess + ReturnToLobby).");
            try { GameManager.MissionSuccess(); } catch (Exception e) { Core.Log.Warning($"MissionSuccess threw {e.GetType().Name}: {e.Message}"); }
            try { GameManager.ReturnToLobby(false); } catch (Exception e) { Core.Log.Warning($"ReturnToLobby threw {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>End in a dungeon: back to the hub with the run kept at this floor (the game's forfeit).</summary>
        public static void ForceSurface()
        {
            if (!PhotonNetwork.IsMasterClient) { Rewards.Toast("Only the host can surface the party."); return; }
            ReconLog.Headline("Hotkey: surfacing (MissionAbort + ReturnToLobby).");
            try { GameManager.MissionAbort(); } catch (Exception e) { Core.Log.Warning($"MissionAbort threw {e.GetType().Name}: {e.Message}"); }
            try { GameManager.ReturnToLobby(false); } catch (Exception e) { Core.Log.Warning($"ReturnToLobby threw {e.GetType().Name}: {e.Message}"); }
        }
    }
}
