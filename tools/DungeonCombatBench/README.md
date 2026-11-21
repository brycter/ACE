# ACE dungeon combat benchmark

This harness loads Holtburg Dungeon cell `0x01F60289` from the retail DATs. It places real ACE players and four Tusker Guards together, checks that all players are in that cell and know one another, and puts 32 items into each player's inventory. The simulation runs the landblock tick, melee damage calculation, enchantment queries, damage notifications, motion broadcasts, session queues, fragment packing, and loopback UDP transmission. A separate process drains the datagrams so receiver CPU is excluded.

Each fighter attempts two unarmed attacks per second. A Tusker responds to each attempt with one point of damage. Monster aggression is disabled and health is increased to keep the same actors alive. The benchmark seeds the simulation thread's combat RNG and staggers attacks across ticks. It advances session acknowledgements every 15 ticks. It does not measure login, client rendering, external network latency, or the capacity of a mixed-workload production shard.

The [benchmark results](RESULTS.md) include the individual runs and the separate broadcast-only and cache-only builds at 256 players.

## Fixture

Use a disposable ACE database instance on a nondefault loopback port. Initialize the authentication, world and shard schemas from the server revision under test. Import stock weenies 1 (Human), 1629 (Tusker Guard), 45, 273, 2623, 2470 and 5134. A minimal world schema without other spawns keeps the encounter reproducible. The supplied DATs must include the dungeon geometry. The harness creates one synthetic authentication account and can run ordinary shard save operations.

Copy `Source/ACE.Server/Config.js.example` outside the repository. Set `Server.WorldName` to `DungeonCombatBench`, `Server.DatFilesDirectory` to the DAT directory, and `Server.Network.Host` to `127.0.0.1`. Choose two unused adjacent UDP ports for ACE. Set the three MySQL connections to the dedicated local instance. Local endpoint assignments are checked at startup.

## Run

Build the console project in Release mode with .NET 8. An alternate source tree can be selected with `-p:AceSource=<absolute path to Source>`. Run one process at a time and alternate the order of baseline and candidate runs. For an A/B comparison of two server DLLs built from the same dependency tree, copy the complete runner output into separate directories and replace the baseline directory's `ACE.Server.dll` with the baseline build. Use the same runner binary, runtime and GC settings for both.

```powershell
dotnet build tools/DungeonCombatBench/DungeonCombatBench.csproj -c Release
$env:DOTNET_PROCESSOR_COUNT = '4'
$env:DOTNET_TieredCompilation = '0'
dotnet tools/DungeonCombatBench/bin/Release/net8.0/DungeonCombatBench.dll C:/ace-bench/Config.js 128 12 C:/ace-bench/trial.json
```

The arguments are the configuration path, player count, measured seconds, and JSON output path. Each process warms up for three seconds before sampling. The JSON records combat attempts and hits, minimum observer count, UDP packets sent and received, process CPU milliseconds, allocated bytes, managed heap, private bytes, working set and the 99th percentile of tick processing time. The tick timer excludes the pacing sleep. Process CPU and allocations exclude fixture startup. The receiver's totals include warmup. Compare attack attempts, successful hits, and packet delivery before interpreting a pair.
