using ACE.Entity;
using ACE.Server.Network.Structure;

// Store valid reports from the authoritative fixture, then feed the same foreground
// actors, positions, and headings through the normal decoder on the control shard.
internal sealed class DoorwayTrace : IDisposable
{
    private const uint Signature = 0x31524441; // ADR1
    private readonly string path;
    private readonly MemoryStream stream;
    private readonly BinaryWriter writer;
    private readonly BinaryReader reader;
    private readonly int expectedReports;
    private int reports;

    internal bool IsReplay => reader != null;

    internal DoorwayTrace(string path, bool replay, int fighters, int seconds)
    {
        this.path = path;
        expectedReports = fighters * (seconds + 3) * 5; // includes the three-second warmup at 5 Hz
        int byteLength = 12 + expectedReports * 36;
        if (replay)
        {
            using var file = File.OpenRead(path);
            if (file.Length != byteLength) throw new InvalidDataException("Trace length differs from the requested workload.");
            stream = new MemoryStream(byteLength);
            file.CopyTo(stream);
            stream.Position = 0;
            reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Signature || reader.ReadInt32() != fighters || reader.ReadInt32() != seconds)
                throw new InvalidDataException("Doorway trace header differs from the requested workload.");
        }
        else
        {
            stream = new MemoryStream(byteLength);
            writer = new BinaryWriter(stream);
            writer.Write(Signature);
            writer.Write(fighters);
            writer.Write(seconds);
        }
    }

    internal Position Exchange(int actor, Position position)
    {
        reports++;
        if (IsReplay)
        {
            if (reader.ReadInt32() != actor) throw new InvalidDataException("Unexpected actor in doorway trace.");
            return new Position(reader);
        }
        writer.Write(actor);
        writer.Write(position);
        return position;
    }

    internal void Complete()
    {
        if (reports != expectedReports) throw new InvalidDataException("Incomplete doorway trace.");
        if (IsReplay) return;
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.WriteTo(output);
    }

    public void Dispose()
    {
        reader?.Dispose();
        writer?.Dispose();
        stream.Dispose();
    }
}
