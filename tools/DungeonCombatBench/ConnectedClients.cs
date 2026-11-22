using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ACE.Common;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.Enum;
using ACE.Server.Network.Managers;
using Character = ACE.Database.Models.Shard.Character;

// Only launched by the parent against its own fixture. Each account has its own
// loopback socket and goes through ACE's password and cookie handshake.
internal sealed class ConnectedClients : IDisposable
{
    internal record Counts(int Ready, long Received, long SentAcks, int Gaps);
    private record Settings(int Port, int Count, string Password);
    readonly Process process;
    internal Session[] Sessions { get; }
    internal static string Name(int i) => $"capacity-fixture-{i:D4}";
    internal static uint GuidFor(int i) => 0x50020000u + (uint)i;

    internal ConnectedClients(int count)
    {
        var cfg = ConfigManager.Config.Server;
        if (cfg.WorldName != "DungeonCombatBench" || cfg.Network.Host != "127.0.0.1" ||
            count > cfg.Network.MaximumAllowedSessions || count > 1152)
            throw new InvalidOperationException("Expected a dedicated loopback capacity fixture.");
        string password = Environment.GetEnvironmentVariable("ACE_BENCH_PASSWORD")
            ?? throw new InvalidOperationException("Set ACE_BENCH_PASSWORD for the isolated fixture accounts.");
        using var gate = new ReaderWriterLockSlim();
        for (int i = 0; i < count; i++)
        {
            var account = DatabaseManager.Authentication.GetAccountByName(Name(i)) ??
                DatabaseManager.Authentication.CreateAccount(Name(i), password, AccessLevel.Player, IPAddress.Loopback);
            var character = DatabaseManager.Shard.BaseDatabase.GetCharacterStubByGuid(GuidFor(i));
            if (character == null && !DatabaseManager.Shard.BaseDatabase.SaveCharacter(
                    new Character { Id = GuidFor(i), AccountId = account.AccountId, Name = $"Capacity Fighter {i}" }, gate))
                throw new InvalidOperationException("Could not provision fixture character.");
            if (character != null && character.AccountId != account.AccountId)
                throw new InvalidOperationException("Unexpected fixture character owner.");
        }
        typeof(WorldManager).GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { null });
        var info = new ProcessStartInfo(Environment.ProcessPath) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
        };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--fixture-peers");
        process = Process.Start(info);
        try
        {
            process.StandardInput.WriteLine(JsonSerializer.Serialize(new Settings((int)cfg.Network.Port, count, password)));
            var ready = process.StandardOutput.ReadLineAsync();
            var watch = Stopwatch.StartNew();
            while (!ready.IsCompleted && watch.Elapsed < TimeSpan.FromMinutes(4))
            {
                WorldManager.ActionQueue.RunActions();
                WorldManager.DelayManager.RunActions();
                NetworkManager.DoSessionWork();
                Thread.Sleep(10);
            }
            if (!ready.IsCompleted || ready.Result != "ready")
                throw new InvalidOperationException($"Wire login incomplete. ACE registered {NetworkManager.GetAuthenticatedSessionCount()} accounts.");
            Sessions = Enumerable.Range(0, count).Select(i => NetworkManager.Find(Name(i))).ToArray();
            if (Sessions.Any(s => s?.State != SessionState.AuthConnected || s.PendingTermination != null)
                || Sessions.Select(s => s.EndPointC2S).Distinct().Count() != count)
                throw new InvalidOperationException("Peer endpoints or authenticated session state did not match.");
        }
        catch { Dispose(); throw; }
    }

    internal Counts Finish()
    {
        process.StandardInput.WriteLine("finish");
        var line = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new InvalidOperationException("Peer worker failed.");
        return JsonSerializer.Deserialize<Counts>(line);
    }

    public void Dispose()
    {
        process.StandardInput.Close();
        if (!process.HasExited && !process.WaitForExit(3000)) process.Kill();
        process.Dispose();
    }

    internal static async Task RunPeers()
    {
        var settings = JsonSerializer.Deserialize<Settings>(await Console.In.ReadLineAsync());
        if (settings.Count < 2 || settings.Count > 1152 || settings.Port < 1024 || settings.Port >= 65535)
            throw new ArgumentException("Invalid fixture dimensions.");
        using var stop = new CancellationTokenSource();
        var peers = new List<Peer>();
        var tasks = new List<Task>();
        try
        {
            // Poll stdin on a dedicated thread. A closed parent exits the worker during login too.
            var finish = Task.Run(() => Console.ReadLine());
            for (int i = 0; i < settings.Count; i++)
            {
                if (finish.IsCompleted) return;
                var peer = new Peer(settings.Port);
                peers.Add(peer);
                tasks.Add(peer.Receive(Name(i), settings.Password, stop.Token));
                if (i % 2 == 1) { SendAcks(peers); await Task.Delay(50); }
            }
            var watch = Stopwatch.StartNew();
            while (peers.Any(p => !p.Ready))
            {
                if (finish.IsCompleted) return;
                foreach (var t in tasks.Where(t => t.IsCompleted)) await t;
                if (watch.Elapsed > TimeSpan.FromSeconds(50))
                    throw new TimeoutException($"Only {peers.Count(p => p.Ready)} of {peers.Count} peers completed login.");
                SendAcks(peers);
                await Task.Delay(25);
            }
            Console.WriteLine("ready");
            while (!finish.IsCompleted)
            {
                SendAcks(peers);
                await Task.Delay(35);
            }
            // Give queued loopback datagrams time to reach the receiver.
            await Task.Delay(200);
            stop.Cancel();
            await Task.WhenAll(tasks);
            Console.WriteLine(JsonSerializer.Serialize(new Counts(peers.Count(p => p.Ready),
                peers.Sum(p => p.Received), peers.Sum(p => p.SentAcks), peers.Sum(p => p.GapCount))));
        }
        finally
        {
            stop.Cancel();
            foreach (var peer in peers) peer.Dispose();
        }
    }

    static void SendAcks(List<Peer> peers)
    {
        long now = Environment.TickCount64;
        foreach (var peer in peers) peer.Acknowledge(now);
    }

    sealed class Peer : IDisposable
    {
        readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        readonly IPEndPoint login, response;
        readonly SortedSet<uint> gaps = new();
        volatile bool ready;
        volatile ServerPacket challengeAnswer;
        int handshakeRetries;
        long nextChallenge;
        uint contiguous = 1, lastAck;
        ushort id;
        long nextAck;
        public long Received, SentAcks;
        public bool Ready => ready;
        public int GapCount => gaps.Count;

        public Peer(int port)
        {
            login = new IPEndPoint(IPAddress.Loopback, port);
            response = new IPEndPoint(IPAddress.Loopback, port + 1);
            socket.ReceiveBufferSize = 128 * 1024;
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        }

        public async Task Receive(string name, string password, CancellationToken stop)
        {
            var packet = new ServerPacket();
            packet.Header.Flags = PacketHeaderFlags.LoginRequest;
            packet.InitializeDataWriter();
            var writer = packet.DataWriter;
            WriteString(writer, "1802");
            long lengthOffset = packet.Data.Position;
            writer.Write(0u);
            writer.Write((uint)NetAuthType.AccountPassword);
            writer.Write(0u);
            writer.Write((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            WriteString(writer, name);
            WriteString(writer, "");
            var secret = Encoding.ASCII.GetBytes(password);
            writer.Write((uint)secret.Length + 1);
            writer.Write((byte)secret.Length);
            writer.Write(secret);
            while (packet.Data.Position % 4 != 0) writer.Write((byte)0);
            packet.Data.Position = lengthOffset;
            writer.Write((uint)(packet.Data.Length - lengthOffset - 4));
            Send(packet, login);
            var buffer = new byte[1024];
            var header = new PacketHeader();
            try
            {
                while (true)
                {
                    var r = await socket.ReceiveFromAsync(buffer.AsMemory(), SocketFlags.None, login, stop);
                    if (r.ReceivedBytes < PacketHeader.HeaderSize || !(r.RemoteEndPoint.Equals(login) || r.RemoteEndPoint.Equals(response))) continue;
                    Received++;
                    header.Unpack(buffer);
                    if (header.HasFlag(PacketHeaderFlags.ConnectRequest))
                    {
                        if (r.ReceivedBytes < 40) throw new InvalidDataException("Incomplete challenge.");
                        id = checked((ushort)BitConverter.ToUInt32(buffer, 36));
                        var answer = new ServerPacket();
                        answer.Header.Flags = PacketHeaderFlags.ConnectResponse;
                        answer.Header.Id = id;
                        answer.InitializeDataWriter();
                        answer.DataWriter.Write(BitConverter.ToUInt64(buffer, 28));
                        nextChallenge = Environment.TickCount64 + 250;
                        Send(answer, response);
                        challengeAnswer = answer;
                    }
                    else
                    {
                        lock (gaps)
                        {
                            if (header.Sequence == contiguous + 1)
                            {
                                contiguous++;
                                while (gaps.Remove(contiguous + 1)) contiguous++;
                            }
                            else if (header.Sequence > contiguous + 1) gaps.Add(header.Sequence);
                        }
                        if (header.HasFlag(PacketHeaderFlags.BlobFragments)) ready = true;
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        static void WriteString(BinaryWriter w, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
            while (w.BaseStream.Position % 4 != 0) w.Write((byte)0);
        }

        public void Acknowledge(long now)
        {
            if (!ready)
            {
                var answer = challengeAnswer;
                if (answer != null && now >= nextChallenge && handshakeRetries < 5)
                {
                    nextChallenge = now + 500;
                    handshakeRetries++;
                    Send(answer, response);
                }
                return;
            }
            if (now < nextAck) return;
            uint seq;
            lock (gaps) seq = contiguous;
            var ack = new ServerPacket();
            ack.Header.Flags = PacketHeaderFlags.AckSequence;
            ack.Header.Sequence = 1;
            ack.Header.Id = id;
            ack.InitializeDataWriter();
            ack.DataWriter.Write(seq);
            Send(ack, login);
            SentAcks++;
            nextAck = now + (seq == lastAck ? 1800 : 400) + id % 79;
            lastAck = seq;
        }

        void Send(ServerPacket packet, IPEndPoint destination)
        {
            var bytes = new byte[512];
            packet.CreateReadyToSendPacket(bytes, out int size);
            socket.SendTo(bytes, 0, size, SocketFlags.None, destination);
        }

        public void Dispose() => socket.Dispose();
    }
}
