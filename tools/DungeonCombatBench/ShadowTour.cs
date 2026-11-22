using System.Diagnostics;
using System.Text.Json;
using ACE.Server.Physics;
using ACE.Server.Physics.Animation;
using ACE.Server.Physics.Common;

internal static class ShadowTour
{
    // Isolate the position-commit path from geometry, sockets, and the database.
    internal static void Run(string output)
    {
        const int population = 1024, cellCount = 256, laps = 3;
        const int factoryCalls = 50000;
        for (int i = 0; i < 2000; i++) GC.KeepAlive(Transition.MakeTransition());
        long factoryStart = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < factoryCalls; i++) GC.KeepAlive(Transition.MakeTransition());
        long factoryAllocated = GC.GetAllocatedBytesForCurrentThread() - factoryStart;
        var actors = Enumerable.Range(0, population).Select(i => new PhysicsObj { ID = (uint)i + 1 }).ToArray();
        var transitions = new Transition[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            var cell = new ObjCell(0x01F60100u + (uint)i);
            var step = new Transition();
            step.SpherePath.CurCell = cell;
            step.SpherePath.CurPos.ObjCellID = cell.ID;
            step.CellArray.add_cell(cell.ID, cell);
            transitions[i] = step;
        }
        void Step(int offset)
        {
            for (int i = 0; i < population; i++)
            {
                var step = transitions[(i + offset) % cellCount];
                // The tour supplies the geometry result. The real position-commit method updates registrations.
                actors[i].CurCell = step.SpherePath.CurCell;
                if (!actors[i].SetPositionInternal(step)) throw new InvalidOperationException("Position commit failed.");
            }
        }
        Step(0);
        long heapBefore = GC.GetTotalMemory(true);
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        long bytesBefore = GC.GetTotalAllocatedBytes(true);
        var clock = Stopwatch.StartNew();
        for (int step = 1; step <= laps * cellCount; step++) Step(step);
        clock.Stop();
        long allocated = GC.GetTotalAllocatedBytes(true) - bytesBefore;
        double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        long retainedHeapDelta = GC.GetTotalMemory(true) - heapBefore;
        int entries = actors.Sum(a => a.ShadowObjects.Count);
        int live = actors.Sum(a => a.ShadowObjects.Values.Count(s => s.Cell != null));
        int registered = transitions.Sum(t => t.SpherePath.CurCell.ShadowObjectList.Count);
        if (live != population || registered != population) throw new InvalidOperationException("Incorrect live registrations.");
        File.WriteAllText(output, JsonSerializer.Serialize(new { population, cellCount, laps, entries, live, registered,
            retainedHeapDelta, allocated, cpuMs, elapsedMs = clock.Elapsed.TotalMilliseconds, factoryCalls, factoryAllocated },
            new JsonSerializerOptions { WriteIndented = true }));
        GC.KeepAlive(actors);
        GC.KeepAlive(transitions);
    }
}
