using System.Diagnostics;
using ACE.Entity;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.Managers;
using ACE.Server.Network.Structure;
using ACE.Server.WorldObjects;

internal static partial class Program
{
    // A separate correctness run in an empty stock dungeon, outside capacity measurements.
    static object CheckMovementCourse(Player player)
    {
        var old = new Position(player.Location);
        var destination = new Position(BenchmarkRooms.Arrivals[10]);
        player.Teleport(destination, true);
        if (player.Location.Landblock != destination.Landblock)
            throw new InvalidOperationException("Server-directed teleport failed.");
        player.SetRequestedLocation(old);
        StepCourse(4);
        if (player.Location.Landblock != destination.Landblock)
            throw new InvalidOperationException("Old client report displaced a portal arrival.");
        player.OnTeleportComplete();
        StepCourse(90);
        if (player.Teleporting || player.CurrentLandblock?.Id.Landblock != destination.Landblock ||
            player.PhysicsObj.CurCell.ID >> 16 != destination.Landblock)
            throw new InvalidOperationException("Portal materialization failed.");

        float walked = 0;
        for (int turn = 0; turn < 4 && walked < 0.25f; turn++)
        {
            var start = new Position(player.Location);
            var heading = new Position(start);
            heading.Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, turn * MathF.PI / 2);
            player.SetRequestedLocation(heading);
            StepCourse(3);
            Move(player, new Position(player.Location), 0);
            StepCourse(75);
            walked = (float)Math.Sqrt(player.Location.SquaredDistanceTo(start));
            Move(player, new Position(player.Location), 1);
            StepCourse(36);
        }
        if (walked < 0.25f) throw new InvalidOperationException("Walk input produced no measurable displacement.");
        var stopped = new Position(player.Location);
        StepCourse(36);
        var drift = Math.Sqrt(player.Location.SquaredDistanceTo(stopped));
        if (drift > 0.15) throw new InvalidOperationException($"Drift after key release: {drift:F3} m.");

        var floor = player.Location.PositionZ;
        var jump = new JumpPack { Extent = 0.6f };
        player.HandleActionJump(jump);
        var high = floor;
        for (int i = 0; i < 120; i++)
        {
            StepCourse(1);
            high = Math.Max(high, player.Location.PositionZ);
        }
        bool landed = player.PhysicsObj.TransientState.HasFlag(ACE.Server.Physics.TransientStateFlags.OnWalkable);
        if (!landed || high < floor + 0.10f || Math.Abs(player.Location.PositionZ - floor) > 0.2f)
            throw new InvalidOperationException($"Jump or landing failed. Floor={floor:F3}, apex={high:F3}, end={player.Location.PositionZ:F3}, landed={landed}.");
        return new { teleported = true, staleReportIgnored = true, materialized = true, landed, walkMeters = walked,
            stoppedDriftMeters = drift, jumpHeightMeters = high - floor, finalCell = player.Location.Cell.ToString("X8") };
    }

    static void StepCourse(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            var start = Stopwatch.GetTimestamp();
            SetTime(Timers.WorldStartLoreTime.Ticks + Clock.Elapsed.TotalSeconds);
            WorldManager.ActionQueue.RunActions();
            WorldManager.DelayManager.RunActions();
            LandblockManager.Tick(Timers.PortalYearTicks);
            NetworkManager.DoSessionWork();
            while (Stopwatch.GetElapsedTime(start).TotalSeconds < 1.0 / Hz) Thread.Sleep(1);
        }
    }
}
