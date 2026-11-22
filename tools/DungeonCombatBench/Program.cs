using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ACE.Common;
using ACE.Database;
using ACE.DatLoader;
using ACE.Entity;
using ACE.Entity.Adapter;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Factories;
using ACE.Server.Network;
using ACE.Server.Network.Managers;
using ACE.Server.Network.GameAction;
using ACE.Server.Network.GameAction.Actions;
using ACE.Server.Network.Sequence;
using ACE.Server.Network.Enum;
using ACE.Server.Network.Structure;
using ACE.Server.WorldObjects;
using Character = ACE.Database.Models.Shard.Character;
using StoredBiota = ACE.Database.Models.Shard.Biota;

// An in-process fight in stock Holtburg Dungeon. Use a disposable loopback database.
// Players, monsters, visibility, damage, world ticks and UDP sends use server code.
internal static partial class Program
{
    const uint Cell = 0x01F60289;
    const int Hz = 60;
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly Action<NetworkSession, uint> Ack = typeof(NetworkSession)
        .GetMethod("AcknowledgeSequence", Private).CreateDelegate<Action<NetworkSession, uint>>();
    static readonly Action<double> SetTime = typeof(Timers).GetProperty(nameof(Timers.PortalYearTicks))
        .GetSetMethod(true).CreateDelegate<Action<double>>();
    static readonly List<Player> Players = new();
    static readonly List<Creature> Monsters = new();
    static readonly Motion Swing = new(MotionStance.HandCombat, MotionCommand.PunchFastMed);
    static readonly Stopwatch Clock = new();
    static long attempts;
    static long hits;
    static long returnedHits;
    static ConnectedClients connected;
    static int foreground;
    static readonly List<Creature> otherMonsters = new();
    static long backgroundAttempts, backgroundHits;
    static long moveRequests, appliedMoves;
    static int minSessions = int.MaxValue;
    static readonly Dictionary<int, ACE.Entity.Position> places = new();
    static readonly Dictionary<int, ACE.Entity.Position> previousPositions = new();

    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--shadow-tour") { ShadowTour.Run(args[1]); return; }
        if (args.Length == 1 && args[0] == "--fixture-peers") { ConnectedClients.RunPeers().GetAwaiter().GetResult(); return; }
        if (args.Length == 1 && args[0] == "--receive") { LoopbackSink.Receive(); return; }
        bool course = args.Length == 6 && args[5] == "--check-movement";
        bool movingWorld = args.Length == 6 && args[5] == "--moving-world-loop";
        bool nativeLoop = movingWorld || args.Length == 6 && args[5] == "--world-loop";
        bool movingFight = movingWorld || args.Length == 6 && args[5] == "--moving-fight";
        if (args.Length != 4 && args.Length != 5 && !course && !nativeLoop && !movingFight)
            throw new ArgumentException("Expected isolated Config.js, player count, measured seconds and result path.");
        int count = int.Parse(args[1]), seconds = int.Parse(args[2]);
        if (count < 2 || count > 256 || seconds < 1 || seconds > 300)
            throw new ArgumentOutOfRangeException(nameof(args));
        foreground = count;
        int total = args.Length >= 5 ? int.Parse(args[4]) : count;
        if (total < count || total > 1152) throw new ArgumentOutOfRangeException(nameof(total));
        SeedFight();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ConfigManager.Initialize(args[0]);
        var cfg = ConfigManager.Config;
        if (cfg.Server.Network.Host != "127.0.0.1" || cfg.Server.WorldName != "DungeonCombatBench"
            || new[] { cfg.MySql.Authentication, cfg.MySql.Shard, cfg.MySql.World }.Any(x => x.Host != "127.0.0.1" || x.Port == 3306))
            throw new InvalidOperationException("Use a dedicated local benchmark configuration and non-production database port.");
        DatManager.Initialize(cfg.Server.DatFilesDirectory, true);
        DatabaseManager.Initialize(false);
        if (DatabaseManager.InitializationFailure) throw new InvalidOperationException("Fixture world initialization failed.");
        DatabaseManager.Start();
        try
        {
            GuidManager.Initialize();
            PropertyManager.Initialize();
            _ = WorldManager.ActionQueue;
            SocketManager.Initialize();
            var listeners = (ConnectionListener[])typeof(SocketManager).GetField("listeners", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            using var peers = args.Length >= 5 ? new ConnectedClients(total) : null;
            connected = peers;
            using var receiver = peers == null ? new LoopbackSink() : null;
            var endpoint = receiver?.Endpoint;
            var room = LandblockManager.GetLandblock(new LandblockId(Cell), false, true);
            if (!room.PhysicsLandblock.IsDungeon) throw new InvalidOperationException("Expected indoor physics.");
            if (movingFight) StartDoorwayFight(count);
            var account = DatabaseManager.Authentication.GetAccountByName("dungeon-bench") ??
                DatabaseManager.Authentication.CreateAccount("dungeon-bench", Guid.NewGuid().ToString(), AccessLevel.Player, IPAddress.Loopback);
            var human = DatabaseManager.World.GetCachedWeenie(1u);
            var tusker = DatabaseManager.World.GetCachedWeenie(1629u);
            var items = new uint[] { 45, 273, 2623, 2470, 5134 }.Select(id => DatabaseManager.World.GetCachedWeenie(id) ?? throw new InvalidOperationException($"Missing item {id}")).ToArray();
            for (int i = 0; i < total; i++)
            {
                uint id = peers == null ? (uint)(0x50010000 + i) : ConnectedClients.GuidFor(i);
                var session = peers?.Sessions[i] ?? new Session(listeners[0], endpoint, (ushort)(i + 1), 1);
                if (peers == null) session.SetAccount(account.AccountId, "dungeon-bench", AccessLevel.Player);
                var character = peers != null ? session.Characters.Single(c => c.Id == id) : new Character { Id = id, AccountId = account.AccountId, Name = $"Fighter {i}" };
                var player = new Player(human.ConvertToBiota(id), Array.Empty<StoredBiota>(), Array.Empty<StoredBiota>(), character, session);
                session.SetPlayer(player);
                player.Name = character.Name;
                int area = (i - count) / 32;
                var localRoom = room;
                if (i < count) player.Location = movingFight ? DoorwayStart(i) : Spot(i);
                else
                {
                    var position = new ACE.Entity.Position(BenchmarkRooms.Arrivals[area]);
                    localRoom = LandblockManager.GetLandblock(new LandblockId(position.Cell), false, true);
                    player.Location = position;
                }
                player.AttackHeight = AttackHeight.Medium;
                player.Attackable = true;
                if (!(i < count ? localRoom.AddWorldObject(player) : TryPlace(localRoom, player, i % 32)))
                    throw new InvalidOperationException($"Failed to place fighter {i} at {player.Location.Cell:X8}.");
                player.Hidden = false;
                player.SetCombatMode(CombatMode.Melee);
                player.GetCreatureSkill(Skill.LightWeapons).InitLevel = 300;
                player.Health.StartingValue = 100000;
                for (int j = 0; j < 32; j++)
                {
                    var item = WorldObjectFactory.CreateWorldObject(items[j % items.Length], new ObjectGuid((uint)(0x91000000 + i * 32 + j)));
                    if (!player.TryAddToInventory(item, j, true, false))
                        throw new InvalidOperationException($"Inventory insertion failed for fighter {i}, slot {j}.");
                }
                player.UpdateVital(player.Health, player.Health.MaxValue);
                Players.Add(player);
                if (peers != null)
                {
                    PlayerManager.AddOfflinePlayer(player);
                    if (!PlayerManager.SwitchPlayerFromOfflineToOnline(player))
                        throw new InvalidOperationException("Player failed to enter online map.");
                    session.State = ACE.Server.Network.Enum.SessionState.WorldConnected;
                    if (i >= count && (i-count) % 32 == 0)
                    {
                        var mob = new Creature(tusker, new ObjectGuid(0x90020000u + (uint)area));
                        mob.Location = new ACE.Entity.Position(player.Location);
                        mob.Tolerance = Tolerance.NoAttack;
                        mob.Health.StartingValue = 100000;
                        if (!TryPlace(localRoom, mob, 32))
                            throw new InvalidOperationException($"Could not place group {area} monster in {player.Location.Cell:X8}.");
                        mob.UpdateVital(mob.Health, mob.Health.MaxValue);
                        otherMonsters.Add(mob);
                    }
                    if (i >= count && (i-count)%4 == 1)
                        {
                        places.Add(i, new ACE.Entity.Position(player.Location));
                        previousPositions.Add(i, new ACE.Entity.Position(player.Location));
                    }
                    if (i % 16 == 15) NetworkManager.DoSessionWork();
                }
            }
            for (int i = 0; i < 4; i++)
            {
                var monster = new Creature(tusker, new ObjectGuid((uint)(0x90010000 + i)));
                monster.Location = movingFight ? DoorwayStart(i, monster: true) : Spot(i + count);
                monster.Tolerance = Tolerance.NoAttack;
                monster.AttackHeight = AttackHeight.Medium;
                monster.Health.StartingValue = 100000;
                if (!room.AddWorldObject(monster)) throw new InvalidOperationException($"Failed to place tusker {i}.");
                monster.UpdateVital(monster.Health, monster.Health.MaxValue);
                Monsters.Add(monster);
            }
            int observersMin = Players.Take(count).Min(p => p.PhysicsObj.ObjMaint.GetKnownPlayersCount());
            if (observersMin < count - 1) throw new InvalidOperationException($"Only {observersMin} observers for {count} players.");
            int occupiedCells = Players.Select(p => p.PhysicsObj.CurCell.ID).Distinct().Count();
            if ((peers == null && occupiedCells != 1) || Players[0].PhysicsObj.CurCell.ID != Cell)
                throw new InvalidOperationException("Fighters must occupy the same room.");
            VerifyCache(Players[0].Inventory.Values.First());
            int maxBackgroundObservers = Players.Skip(count).Select(p => p.PhysicsObj.ObjMaint.GetKnownPlayersCount()).DefaultIfEmpty().Max();
            if (maxBackgroundObservers > 32 || (peers != null && PlayerManager.GetOnlineCount() != total))
                throw new InvalidOperationException($"Unexpected background visibility ({maxBackgroundObservers}) or online population.");
            bool positionCorrectionVerified = false, serverJumpVerified = false;
            Clock.Start();
            if (peers != null && cfg.Server.ServerAuthoritativeMovement)
            {
                var witness = Players[0];
                var original = new ACE.Entity.Position(witness.Location);
                var report = new ACE.Entity.Position(original);
                report.PositionX += 8;
                var oldSequence = witness.Sequences.GetCurrentSequence(SequenceType.ObjectForcePosition);
                witness.SetRequestedLocation(report);
                witness.UpdateObjectPhysics();
                if (witness.Location.SquaredDistanceTo(original) > 0.1f ||
                    oldSequence.SequenceEqual(witness.Sequences.GetCurrentSequence(SequenceType.ObjectForcePosition)))
                    throw new InvalidOperationException("Stationary position reconciliation failed.");
                positionCorrectionVerified = true;
                if (!witness.PhysicsObj.WeenieObj.InqJumpVelocity(0.5f, out var expectedZ))
                    throw new InvalidOperationException("Could not derive jump speed.");
                var jump = new JumpPack { Extent = 0.5f, Velocity = System.Numerics.Vector3.Zero };
                witness.HandleActionJump(jump);
                if (jump.Velocity.Z <= 0 || Math.Abs(jump.Velocity.Z - expectedZ) > 0.001f)
                    throw new InvalidOperationException($"Jump speed differs from server calculation ({jump.Velocity.Z} vs {expectedZ}).");
                serverJumpVerified = true;
            }
            if (nativeLoop)
            {
                var nativeResult = ExerciseWorldLoop(total, seconds, peers, positionCorrectionVerified, serverJumpVerified);
                File.WriteAllText(args[3], JsonSerializer.Serialize(nativeResult, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            if (course)
            {
                if (!cfg.Server.ServerAuthoritativeMovement) throw new InvalidOperationException("The movement course requires authority.");
                var checks = CheckMovementCourse(Players[count - 1]);
                var clientCounts = peers.Finish();
                File.WriteAllText(args[3], JsonSerializer.Serialize(new { checks, clientCounts }, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            Run(3 * Hz);
            if (movingFight) ResetDoorwayMetrics();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            var process = Process.GetCurrentProcess();
            process.Refresh();
            long privateStart = process.PrivateMemorySize64, workingSetStart = process.WorkingSet64;
            SeedFight();
            long heapStart = GC.GetTotalMemory(false);
            long allocated = GC.GetTotalAllocatedBytes(true);
            var cpu = process.TotalProcessorTime;
            long packets = NetworkStatistics.S2C_Packets_Aggregate;
            attempts = hits = returnedHits = 0;
            backgroundAttempts = backgroundHits = moveRequests = appliedMoves = 0;
            minSessions = int.MaxValue;
            var elapsed = Stopwatch.StartNew();
            var ticks = Run(seconds * Hz);
            elapsed.Stop();
            var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            packets = NetworkStatistics.S2C_Packets_Aggregate - packets;
            process.Refresh();
            long privateEnd = process.PrivateMemorySize64, workingSetEnd = process.WorkingSet64;
            var liveHeap = GC.GetTotalMemory(true);
            var doorway = movingFight ? DoorwayResults() : null;
            long totalPacketsSent = NetworkStatistics.S2C_Packets_Aggregate;
            int online = peers == null ? 0 : PlayerManager.GetOnlineCount();
            int authenticated = peers == null ? 0 : NetworkManager.GetAuthenticatedSessionCount();
            var counts = peers?.Finish();
            if (peers != null && (minSessions != total || online != total || authenticated != total ||
                counts.Ready != total || peers.Sessions.Any(s => s.State != ACE.Server.Network.Enum.SessionState.WorldConnected || s.PendingTermination != null)))
                throw new InvalidOperationException("Population did not stay connected.");
            long totalPacketsReceived = counts?.Received ?? receiver.Finish();
            if (totalPacketsReceived == 0) throw new InvalidOperationException("UDP sink received no combat packets.");
            Array.Sort(ticks);
            if (hits == 0 || returnedHits == 0 || packets < count) throw new InvalidOperationException("Incomplete fight.");
            var result = new { authoritativeMovement = cfg.Server.ServerAuthoritativeMovement, positionCorrectionVerified, serverJumpVerified, players = count, total, online, authenticated, minSessions = peers == null ? 0 : minSessions,
                counts, doorway, maxBackgroundObservers, backgroundAreas = otherMonsters.Count, backgroundAttempts, backgroundHits,
                moveRequests, appliedMoves, overBudgetTicks = ticks.Count(x => x > 1000.0 / Hz),
                p999TickMs = ticks[(int)Math.Ceiling(ticks.Length * .999)-1], maxTickMs = ticks[^1],
                clientCrcErrors = NetworkStatistics.C2S_CRCErrors_Aggregate, monsters = Monsters.Count, cell = Cell.ToString("X8"), observersMin,
                ticks = ticks.Length, attempts, hits, returnedHits, packets, totalPacketsSent, totalPacketsReceived, inventoryItems = Players.Sum(p => p.Inventory.Count), elapsedMs = elapsed.Elapsed.TotalMilliseconds, cpuMs,
                allocatedBytes = allocated, heapStart, liveHeap, privateStart, privateEnd, workingSetStart, workingSetEnd, occupiedCells,
                processPeakWorkingSet = process.PeakWorkingSet64, p99TickMs = ticks[(int)Math.Ceiling(ticks.Length * .99)-1],
                serverGc = System.Runtime.GCSettings.IsServerGC, runtime = Environment.Version.ToString() };
            File.WriteAllText(args[3], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { DatabaseManager.Stop(); }
    }

    static bool TryPlace(Landblock room, WorldObject actor, int slot)
    {
        var origin = new ACE.Entity.Position(actor.Location);
        for (int j = 0; j < 65; j++)
        {
            var pos = new ACE.Entity.Position(origin);
            if (j != 0)
            {
                var angle = (slot + j) * 2.39996323;
                var radius = 0.6 * Math.Sqrt(j);
                pos.PositionX += (float)(radius * Math.Cos(angle));
                pos.PositionY += (float)(radius * Math.Sin(angle));
            }
            actor.Location = pos;
            if (room.AddWorldObject(actor)) return true;
        }
        return false;
    }

    static void SeedFight()
    {
        // Apply the same RNG stream on the simulation thread in each A/B process.
        var rng = (ThreadLocal<Random>)typeof(ThreadSafeRandom)
            .GetField("random", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        rng.Value = new Random(61096);
    }

    static void VerifyCache(WorldObject item)
    {
        var property = PropertyInt.DamageRating;
        var manager = item.EnchantmentManager;
        var effect = new ACE.Entity.Models.PropertiesEnchantmentRegistry {
            SpellId = 123, Duration = 60, StatModType = EnchantmentTypeFlags.Int |
                EnchantmentTypeFlags.SingleStat | EnchantmentTypeFlags.Additive,
            StatModKey = (uint)property, StatModValue = 7
        };
        item.Biota.PropertiesEnchantmentRegistry.Add(effect);
        if (manager.GetRating(property) != 7 || manager.GetRating(property) != 7)
            throw new InvalidOperationException("Cache fill or hit failed.");
        manager.RemoveAllEnchantments();
        if (manager.GetRating(property) != 0)
            throw new InvalidOperationException("Removed enchantment still affected the item.");
    }

    static Position Spot(int i) => new(Cell, 96.7f + (i % 5) * .16f, -10f + ((i / 5) % 5) * .16f, 0, 0, 0, -.694721f, -.719279f);

    // Decode the same key states as an AC client at the normal game-action handler.
    // The separate peers handle wire authentication, UDP reads and acknowledgements.
    static void Move(Player player, ACE.Entity.Position report, int phase)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)GameActionType.MoveToState);
        if (phase == 0 || phase == 2)
        {
            writer.Write((uint)RawMotionFlags.ForwardCommand);
            writer.Write((uint)(phase == 0 ? MotionCommand.WalkForward : MotionCommand.WalkBackwards));
        }
        else writer.Write(0u);
        writer.Write(report);
        writer.Write(0UL);
        writer.Write((byte)1);
        while (stream.Position % 4 != 0) writer.Write((byte)0);
        stream.Position = 0;
        GameActionMoveToState.Handle(new ClientMessage(stream), player.Session);
    }

    static void DrivePlayers(int tick)
    {
        if (doorwayWalkers != null) WalkDoorway(tick);
        // Offset attack phases so a larger population increases traffic at a constant per-player rate.
        for (int i = tick % 30; i < foreground; i += 30)
        {
            var player = Players[i];
            var mob = Monsters[i % Monsters.Count];
            if (mob.Health.Current < 10) mob.UpdateVital(mob.Health, mob.Health.MaxValue);
            if (player.Health.Current < 10) player.UpdateVital(player.Health, player.Health.MaxValue);
            player.EnqueueBroadcastMotion(Swing, applyPhysics: false);
            attempts++;
            if (player.DamageTarget(mob, player)?.HasDamage == true) hits++;
            mob.EnqueueBroadcastMotion(Swing, applyPhysics: false);
            if (player.TakeDamage(mob, DamageType.Bludgeon, 1, BodyPart.Chest) > 0) returnedHits++;
        }
        if (connected != null)
        {
            // One quarter of the distributed population fights twice a second.
            for (int j = tick % 30; foreground + j * 4 < Players.Count; j += 30)
            {
                var player = Players[foreground + j * 4];
                var mob = otherMonsters[j / 8];
                if (mob.Health.Current < 10) mob.UpdateVital(mob.Health, mob.Health.MaxValue);
                player.EnqueueBroadcastMotion(Swing, applyPhysics: false);
                backgroundAttempts++;
                if (player.DamageTarget(mob, player)?.HasDamage == true) backgroundHits++;
                mob.EnqueueBroadcastMotion(Swing, applyPhysics: false);
                player.TakeDamage(mob, DamageType.Bludgeon, 1, BodyPart.Chest);
            }
            // A different quarter sends a staggered walking-position update every second.
            for (int j = tick % Hz; foreground + j * 4 + 1 < Players.Count; j += Hz)
            {
                int index = foreground + j * 4 + 1;
                var player = Players[index];
                if (player.Location.SquaredDistanceTo(previousPositions[index]) > 0.0025f) appliedMoves++;
                previousPositions[index] = new ACE.Entity.Position(player.Location);
                var report = new ACE.Entity.Position(places[index]);
                int phase = tick / Hz % 4;
                report.PositionY += phase == 1 || phase == 2 ? 2.5f : 0;
                Move(player, report, phase);
                moveRequests++;
            }
        }
    }

    static double[] Run(int tickCount)
    {
        var timings = new double[tickCount];
        var start = Clock.Elapsed.TotalSeconds;
        for (int tick = 0; tick < tickCount; tick++)
        {
            double due = start + tick / (double)Hz;
            while (Clock.Elapsed.TotalSeconds < due) Thread.Sleep(1);
            var stamp = Stopwatch.GetTimestamp();
            SetTime(Timers.WorldStartLoreTime.Ticks + Clock.Elapsed.TotalSeconds);
            DrivePlayers(tick);
            if (connected != null)
            {
                NetworkManager.InboundMessageQueue.RunActions();
                PlayerManager.Tick();
            }
            WorldManager.ActionQueue.RunActions();
            WorldManager.DelayManager.RunActions();
            LandblockManager.Tick(Timers.PortalYearTicks);
            if (connected != null)
                minSessions = Math.Min(minSessions, NetworkManager.DoSessionWork());
            else foreach (var player in Players)
            {
                var network = player.Session.Network;
                network.Update();
                if (tick % 15 == 0) Ack(network, network.ConnectionData.PacketSequence.CurrentValue);
            }
            timings[tick] = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
        }
        return timings;
    }
}
