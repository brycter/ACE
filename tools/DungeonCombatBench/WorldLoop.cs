using System.Diagnostics;
using ACE.Common;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.Enum;
using ACE.Server.Network.Managers;

internal static partial class Program
{
    // Drive inputs on ACE's actual world thread. Its housekeeping, housing, physics and
    // network dispatch run on the scheduler used by the server executable.
    static object ExerciseWorldLoop(int total, int seconds, ConnectedClients peers, bool corrected, bool jumped)
    {
        ConfigManager.Config.Server.LandblockPreloading = false;
        HouseManager.Initialize();
        WorldManager.Initialize();
        if (!SpinWait.SpinUntil(() => WorldManager.WorldActive, TimeSpan.FromSeconds(10)))
            throw new TimeoutException("World thread failed to start.");
        try
        {
            ScheduleInputs(3 * Hz, total);
            var process = Process.GetCurrentProcess();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            attempts = hits = returnedHits = backgroundAttempts = backgroundHits = moveRequests = appliedMoves = 0;
            minSessions = total;
            var allocated = GC.GetTotalAllocatedBytes(true);
            var cpu = process.TotalProcessorTime;
            var elapsed = Stopwatch.StartNew();
            var delays = ScheduleInputs(seconds * Hz, total);
            elapsed.Stop();
            var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            process.Refresh();
            var workingSet = process.WorkingSet64;
            var privateBytes = process.PrivateMemorySize64;
            var liveHeap = GC.GetTotalMemory(true);
            StopAndJoinWorld();
            if (NetworkManager.GetAuthenticatedSessionCount() != total || PlayerManager.GetOnlineCount() != total ||
                minSessions != total || peers.Sessions.Any(s => s.State != SessionState.WorldConnected || s.PendingTermination != null))
                throw new InvalidOperationException("World loop lost a participant.");
            var totalPacketsSent = NetworkStatistics.S2C_Packets_Aggregate;
            var counts = peers.Finish();
            Array.Sort(delays);
            return new { scheduler = "WorldManager.Initialize", total, foreground, seconds,
                authority = ConfigManager.Config.Server.ServerAuthoritativeMovement,
                authenticated = NetworkManager.GetAuthenticatedSessionCount(), online = PlayerManager.GetOnlineCount(),
                minSessions, counts, totalPacketsSent, cpuMs, elapsedMs = elapsed.Elapsed.TotalMilliseconds,
                workingSet, privateBytes, liveHeap, allocated, attempts, hits, returnedHits, backgroundAttempts, backgroundHits,
                moveRequests, appliedMoves, positionCorrectionVerified = corrected, serverJumpVerified = jumped,
                p99ScheduledInputMs = delays[(int)Math.Ceiling(delays.Length * .99) - 1], worstScheduledInputMs = delays[^1],
                inputsOverBudget = delays.Count(t => t > 1000d / Hz), inputBatches = delays.Length,
                crcErrors = NetworkStatistics.C2S_CRCErrors_Aggregate };
        }
        finally { if (WorldManager.WorldActive) StopAndJoinWorld(); }
    }

    static void StopAndJoinWorld()
    {
        WorldManager.StopWorld();
        if (!SpinWait.SpinUntil(() => !WorldManager.WorldActive, TimeSpan.FromSeconds(15)))
            throw new TimeoutException("The world thread failed to finish its current loop.");
    }

    static double[] ScheduleInputs(int count, int expected)
    {
        var timings = new double[count];
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            int tick = i;
            double due = tick * (1000d / Hz);
            while (timer.Elapsed.TotalMilliseconds < due) Thread.Sleep(1);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            WorldManager.EnqueueAction(new ActionEventDelegate(() =>
            {
                try
                {
                    DrivePlayers(tick);
                    if (tick % Hz == 0)
                        minSessions = Math.Min(minSessions, NetworkManager.GetSessionCount());
                    timings[tick] = timer.Elapsed.TotalMilliseconds - due;
                    completed.SetResult();
                }
                catch (Exception ex) { completed.SetException(ex); }
            }));
            if (!completed.Task.Wait(TimeSpan.FromSeconds(10)) || minSessions < expected)
                throw new InvalidOperationException("Game action stalled or population dropped.");
        }
        return timings;
    }
}
