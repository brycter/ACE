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
using ACE.Server.WorldObjects;
using Character = ACE.Database.Models.Shard.Character;
using StoredBiota = ACE.Database.Models.Shard.Biota;

// An in-process fight in stock Holtburg Dungeon. Use a disposable loopback database.
// Players, monsters, visibility, damage, world ticks and UDP sends use server code.
internal static class Program
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

    static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--receive") { LoopbackSink.Receive(); return; }
        if (args.Length != 4)
            throw new ArgumentException("Expected isolated Config.js, player count, measured seconds and result path.");
        int count = int.Parse(args[1]), seconds = int.Parse(args[2]);
        if (count < 2 || count > 256 || seconds < 1 || seconds > 300)
            throw new ArgumentOutOfRangeException(nameof(args));
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
            using var receiver = new LoopbackSink();
            var endpoint = receiver.Endpoint;
            var room = LandblockManager.GetLandblock(new LandblockId(Cell), false, true);
            if (!room.PhysicsLandblock.IsDungeon) throw new InvalidOperationException("Expected indoor physics.");
            var account = DatabaseManager.Authentication.GetAccountByName("dungeon-bench") ??
                DatabaseManager.Authentication.CreateAccount("dungeon-bench", Guid.NewGuid().ToString(), AccessLevel.Player, IPAddress.Loopback);
            var human = DatabaseManager.World.GetCachedWeenie(1u);
            var tusker = DatabaseManager.World.GetCachedWeenie(1629u);
            var items = new uint[] { 45, 273, 2623, 2470, 5134 }.Select(id => DatabaseManager.World.GetCachedWeenie(id) ?? throw new InvalidOperationException($"Missing item {id}")).ToArray();
            for (int i = 0; i < count; i++)
            {
                uint id = (uint)(0x50010000 + i);
                var session = new Session(listeners[0], endpoint, (ushort)(i + 1), 1);
                session.SetAccount(account.AccountId, "dungeon-bench", AccessLevel.Player);
                var character = new Character { Id = id, AccountId = account.AccountId, Name = $"Fighter {i}" };
                var player = new Player(human.ConvertToBiota(id), Array.Empty<StoredBiota>(), Array.Empty<StoredBiota>(), character, session);
                session.SetPlayer(player);
                player.Name = character.Name;
                player.Location = Spot(i);
                player.AttackHeight = AttackHeight.Medium;
                player.Attackable = true;
                if (!room.AddWorldObject(player)) throw new InvalidOperationException($"Failed to place fighter {i}.");
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
            }
            for (int i = 0; i < 4; i++)
            {
                var monster = new Creature(tusker, new ObjectGuid((uint)(0x90010000 + i)));
                monster.Location = Spot(i + count);
                monster.Tolerance = Tolerance.NoAttack;
                monster.AttackHeight = AttackHeight.Medium;
                monster.Health.StartingValue = 100000;
                if (!room.AddWorldObject(monster)) throw new InvalidOperationException($"Failed to place tusker {i}.");
                monster.UpdateVital(monster.Health, monster.Health.MaxValue);
                Monsters.Add(monster);
            }
            int observersMin = Players.Min(p => p.PhysicsObj.ObjMaint.GetKnownPlayersCount());
            if (observersMin < count - 1) throw new InvalidOperationException($"Only {observersMin} observers for {count} players.");
            int occupiedCells = Players.Select(p => p.PhysicsObj.CurCell.ID).Distinct().Count();
            if (occupiedCells != 1 || Players[0].PhysicsObj.CurCell.ID != Cell)
                throw new InvalidOperationException("Fighters must occupy the same room.");
            VerifyCache(Players[0].Inventory.Values.First());
            Clock.Start();
            Run(3 * Hz);
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
            var elapsed = Stopwatch.StartNew();
            var ticks = Run(seconds * Hz);
            elapsed.Stop();
            var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            packets = NetworkStatistics.S2C_Packets_Aggregate - packets;
            process.Refresh();
            long privateEnd = process.PrivateMemorySize64, workingSetEnd = process.WorkingSet64;
            var liveHeap = GC.GetTotalMemory(true);
            long totalPacketsSent = NetworkStatistics.S2C_Packets_Aggregate;
            long totalPacketsReceived = receiver.Finish();
            if (totalPacketsReceived == 0) throw new InvalidOperationException("UDP sink received no combat packets.");
            Array.Sort(ticks);
            if (hits == 0 || returnedHits == 0 || packets < count) throw new InvalidOperationException("Incomplete fight.");
            var result = new { players = count, monsters = Monsters.Count, cell = Cell.ToString("X8"), observersMin,
                ticks = ticks.Length, attempts, hits, returnedHits, packets, totalPacketsSent, totalPacketsReceived, inventoryItems = Players.Sum(p => p.Inventory.Count), elapsedMs = elapsed.Elapsed.TotalMilliseconds, cpuMs,
                allocatedBytes = allocated, heapStart, liveHeap, privateStart, privateEnd, workingSetStart, workingSetEnd, occupiedCells,
                processPeakWorkingSet = process.PeakWorkingSet64, p99TickMs = ticks[(int)Math.Ceiling(ticks.Length * .99)-1],
                serverGc = System.Runtime.GCSettings.IsServerGC, runtime = Environment.Version.ToString() };
            File.WriteAllText(args[3], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { DatabaseManager.Stop(); }
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
            // Offset attack phases so a larger population increases traffic at a constant per-player rate.
            for (int i = tick % 30; i < Players.Count; i += 30)
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
            WorldManager.ActionQueue.RunActions();
            WorldManager.DelayManager.RunActions();
            LandblockManager.Tick(Timers.PortalYearTicks);
            foreach (var player in Players)
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
