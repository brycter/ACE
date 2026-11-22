using System.Numerics;
using ACE.Common;
using ACE.Entity;
using ACE.Server.Network.Sequence;
using ACE.Server.Physics.Common;
using Position = ACE.Entity.Position;

internal static partial class Program
{
    sealed class DoorwayWalker
    {
        public bool Outbound = true;
        public Vector3 Previous;
        public uint Cell;
        public int Crossings;
        public double Distance;
        public ushort ForceSequence;
    }

    static DoorwayWalker[] doorwayWalkers;
    static DoorwayTrace doorwayTrace;
    static uint adjacentCell;
    static Vector3 doorwayCenter, outbound, across;

    static void StartDoorwayFight(int count)
    {
        if (!ConfigManager.Config.Server.ServerAuthoritativeMovement && doorwayTrace?.IsReplay != true)
            throw new InvalidOperationException("Record with server movement authority or replay a recorded doorway route.");
        var room = (EnvCell)ObjCell.GetVisible(Cell);
        var origin = Spot(0).Pos;
        float nearest = float.PositiveInfinity;
        foreach (var portal in room.Portals)
        {
            var neighbor = room.GetVisible(portal.OtherCellId);
            if (neighbor == null) continue;
            var polygon = room.CellStructure.Polygons[portal.PolygonId];
            var vertices = polygon.Vertices.Select(v => room.Pos.LocalToGlobal(v.Origin)).ToArray();
            var center = vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / vertices.Length;
            center.Z = vertices.Min(v => v.Z) + 0.03f;
            var normal = room.Pos.LocalToGlobalVec(polygon.Plane.Normal);
            normal.Z = 0;
            if (normal.LengthSquared() < 0.8f) continue;
            normal = Vector3.Normalize(normal);
            var chest = center + Vector3.UnitZ;
            if (!neighbor.point_in_cell(chest + normal)) normal = -normal;
            if (!room.point_in_cell(chest - normal * 1.8f) || !neighbor.point_in_cell(chest + normal * 1.8f)) continue;
            float distance = Vector3.DistanceSquared(origin, center);
            if (distance >= nearest) continue;
            nearest = distance;
            adjacentCell = neighbor.ID;
            doorwayCenter = center;
            outbound = normal;
        }
        if (adjacentCell == 0) throw new InvalidOperationException("Could not find a traversable shared dungeon doorway.");
        across = new Vector3(-outbound.Y, outbound.X, 0);
        doorwayWalkers = Enumerable.Range(0, count).Select(_ => new DoorwayWalker()).ToArray();
        Console.WriteLine($"Doorway {Cell:X8} to {adjacentCell:X8} at {doorwayCenter}, heading {outbound}");
    }

    static Position DoorwayStart(int i, bool monster = false)
    {
        var pos = doorwayCenter - outbound * (monster ? 3.2f + (i / 2) * 0.4f : 0.8f + (i % 9) * 0.09f);
        pos += across * (monster ? (i % 2 == 0 ? 1.3f : -1.3f) : (i % 7 - 3) * 0.13f);
        var location = new Position(Cell, pos, Quaternion.Identity);
        location.Rotate(outbound);
        return location;
    }

    static ushort ForceSequence(int i) =>
        BitConverter.ToUInt16(Players[i].Sequences.GetCurrentSequence(SequenceType.ObjectForcePosition));

    static void ResetDoorwayMetrics()
    {
        for (int i = 0; i < doorwayWalkers.Length; i++)
        {
            var w = doorwayWalkers[i];
            w.Previous = Players[i].Location.Pos;
            w.Cell = Players[i].Location.Cell;
            w.Distance = 0;
            w.Crossings = 0;
            w.ForceSequence = ForceSequence(i);
        }
    }

    static void SampleDoorway(int i)
    {
        var w = doorwayWalkers[i];
        var location = Players[i].Location;
        if (w.Cell != 0)
        {
            w.Distance += Vector3.Distance(location.Pos, w.Previous);
            if ((w.Cell == Cell && location.Cell == adjacentCell) || (w.Cell == adjacentCell && location.Cell == Cell))
                w.Crossings++;
        }
        w.Cell = location.Cell;
        w.Previous = location.Pos;
    }

    static void WalkDoorway(int tick)
    {
        // Stagger input at 5 Hz per mover. Reverse at the endpoints of the same short lane.
        for (int i = tick % 12; i < doorwayWalkers.Length; i += 12)
        {
            SampleDoorway(i);
            var walker = doorwayWalkers[i];
            var report = new Position(Players[i].Location);
            float progress = Vector3.Dot(report.Pos - doorwayCenter, outbound);
            if (progress >= 1.4f) walker.Outbound = false;
            if (progress <= -1.4f) walker.Outbound = true;
            report.Rotate(walker.Outbound ? outbound : -outbound);
            Move(Players[i], doorwayTrace?.Exchange(i, report) ?? report, 0);
        }
    }

    static object DoorwayResults()
    {
        int corrections = 0, detached = 0;
        for (int i = 0; i < doorwayWalkers.Length; i++)
        {
            SampleDoorway(i);
            corrections += unchecked((ushort)(ForceSequence(i) - doorwayWalkers[i].ForceSequence));
            detached += Players[i].PhysicsObj.ShadowObjects.Values.Count(s => s.Cell == null);
        }
        int movers = doorwayWalkers.Count(w => w.Distance > 0.5);
        int crossers = doorwayWalkers.Count(w => w.Crossings > 0);
        double meters = doorwayWalkers.Sum(w => w.Distance);
        int crossings = doorwayWalkers.Sum(w => w.Crossings);
        if (movers != foreground || crossers != foreground)
            throw new InvalidOperationException($"Doorway workload incomplete: {movers}/{foreground} moved, {crossers} crossed, {crossings} crossings, {meters:F1} m.");
        return new { adjacentCell = adjacentCell.ToString("X8"), movers, crossers, crossings, meters, corrections, detached };
    }
}
