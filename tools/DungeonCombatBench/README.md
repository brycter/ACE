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

## Connected shard and movement tests

Add a fifth argument for the total online population. The below examples each create 1,024 password-authenticated, in-world characters. The first puts 32 in Holtburg Dungeon and the second puts 200 there. The remaining characters occupy stock dungeon portal arrivals in groups of up to 32. Each background group has one Tusker, one quarter of the characters fight, one quarter walk and stop, and the remainder stand in the room. All characters carry 32 items.

In the dedicated fixture configuration set `Server.Network.MaximumAllowedSessions` to 1152. Set the environment variable `ACE_BENCH_PASSWORD` to a fresh password for the fixture, and reuse it for subsequent trials on that same fixture. Run the commands in separate processes.

```powershell
dotnet tools/DungeonCombatBench/bin/Release/net8.0/DungeonCombatBench.dll C:/ace-bench/Config.js 32 75 C:/ace-bench/ordinary.json 1024
dotnet tools/DungeonCombatBench/bin/Release/net8.0/DungeonCombatBench.dll C:/ace-bench/Config.js 200 75 C:/ace-bench/event.json 1024
```

Each peer has its own loopback UDP socket and fixture account. It logs in with ACE's password handshake, waits for character-selection traffic, and acknowledges sequences it has received. ACE loads the account's character list from the shard. The harness places synthetic characters into the real landblock and online-player registries and drives game actions on the server. Walk inputs go through the normal MoveToState decoder. This workload exercises character ticks, world physics, session processing and UDP delivery. It scripts world entry, movement input and combat locally. Native client prediction, latency over the internet, unscripted monster AI and long-running shard activity need separate testing.

To compare movement modes, use identical configurations and change only `Server.ServerAuthoritativeMovement`. The default is `false`. With it set to `true`, ACE derives translation from the server motion interpreter and physics frame. It uses reported position for reconciliation and accepts horizontal facing when the reported position agrees with the server. On the server, jump extent, stamina, burden and skill determine takeoff speed. A stationary-player correction and a jump with server-derived velocity are checked during setup in that mode.

The JSON includes authenticated and online counts, minimum registered sessions during the timed interval, TCP connections to the database when sampled by the comparison driver, processing-time percentiles, ticks over 16.67 ms, peer receipt counts, and remaining out-of-order packet counts. A movement sample is counted when a walker's server position has changed by over 5 cm since its previous sample. The trial results are in [the connected-shard report](SHARD-RESULTS.md).
