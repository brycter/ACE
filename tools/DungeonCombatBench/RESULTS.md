# Holtburg Dungeon combat results

Baseline was ACE commit `6633b528`. The combined server changes are in commit `b147d5bb`. The experiment used four fresh processes per build and headcount, 48 trials in total. Each process ran 3 seconds of warmup and 12 seconds of measured combat. Values in the tables are medians across the four runs. The report also gives CPU differences for each pair. Allocation totals count all managed bytes allocated during those 12 seconds. Live heap is measured after collection. Working set and private bytes are sampled at the end of combat before that collection. All memory figures use MiB (1,048,576 bytes).

At 256 players the combined build reduced median CPU by 4.4%, p99 tick time by 1.77 ms, and the live managed heap by 10.6 MiB. It allocated 139.0 MiB less during combat. At 128 players it used more CPU in every pair. This supports a memory improvement across these rooms and leaves the production player limit open.

## Process CPU and server tick processing

| Players | Baseline CPU (ms) | Combined CPU (ms) | Change | Baseline p99 tick (ms) | Combined p99 tick (ms) |
| --- | --- | --- | --- | --- | --- |
| 8 | 78.1 | 148.4 | +90.0% | 0.44 | 0.45 |
| 32 | 335.9 | 304.7 | -9.3% | 0.93 | 0.94 |
| 64 | 890.6 | 718.8 | -19.3% | 1.95 | 1.72 |
| 128 | 2,062.5 | 2,210.9 | +7.2% | 4.58 | 4.62 |
| 256 | 5,093.8 | 4,867.2 | -4.4% | 12.71 | 10.94 |

Each run executes 720 ticks at a target of 60 Hz, a budget of 16.67 ms per tick. The p99 column measures the time spent processing a tick. The pacing sleep is excluded. Process CPU includes all threads in the ACE benchmark process. The loopback receiver and database run in separate processes.

## Memory

Values run from baseline to combined. Working set measures resident process memory, which includes GC segments and native memory. The live heap covers managed objects. Allocation savings measure reduced GC traffic during combat.

| Players | Allocated (MiB) | Live heap (MiB) | Working set (MiB) | Private bytes (MiB) |
| --- | --- | --- | --- | --- |
| 8 | 12.0 → 11.9 | 130.5 → 130.0 | 391.8 → 399.6 | 336.1 → 344.0 |
| 32 | 65.1 → 63.3 | 135.1 → 133.8 | 407.1 → 416.9 | 351.9 → 361.3 |
| 64 | 209.3 → 201.7 | 141.9 → 139.6 | 533.9 → 515.9 | 479.8 → 461.7 |
| 128 | 788.0 → 755.2 | 158.2 → 153.6 | 870.1 → 848.0 | 820.1 → 797.5 |
| 256 | 3,044.8 → 2,905.8 | 200.8 → 190.2 | 974.1 → 969.3 | 931.5 → 928.0 |

## Individual changes at 256 players

| Build | CPU (ms) | Allocated (MiB) | Live heap (MiB) | Working set (MiB) | p99 tick (ms) |
| --- | --- | --- | --- | --- | --- |
| baseline | 5,093.8 | 3,044.8 | 200.8 | 974.1 | 12.71 |
| broadcast | 4,882.8 | 2,905.3 | 198.1 | 982.3 | 12.45 |
| caches | 5,265.6 | 3,044.5 | 192.9 | 994.7 | 11.34 |
| patched | 4,867.2 | 2,905.8 | 190.2 | 969.3 | 10.94 |

At 256 players each process attempted 6,144 attacks and delivered 6,144 return hits. Median successful player hits were 2,248 for baseline and 2,249 for combined. Median timed UDP sends were 563,209 and 563,083, respectively.

The broadcast build shares serialized broadcast fragments and precomputes their payload checksums. The caches build allocates enchantment lookups and visibility maps on demand. The patched build includes both changes. At 256 players each build occupied each position in the run order once. At smaller headcounts the baseline and combined run order alternated.

## Run-to-run CPU variation

Each value is combined minus baseline process CPU time in milliseconds for the same round. Negative values indicate that the combined build used less CPU.

| Players | Round 1 | Round 2 | Round 3 | Round 4 | Rounds with lower CPU |
| --- | --- | --- | --- | --- | --- |
| 8 | +203.1 | -31.2 | +125.0 | -31.2 | 2 |
| 32 | +46.9 | -187.5 | -109.4 | +156.2 | 2 |
| 64 | -93.8 | -453.1 | -203.1 | +93.8 | 3 |
| 128 | +250.0 | +46.9 | +78.1 | +218.8 | 0 |
| 256 | -625.0 | -437.5 | +171.9 | -15.6 | 3 |

## Workload and checks

All 48 runs placed every player in Holtburg Dungeon cell `0x01F60289`, with at least every other player visible, four Tusker Guards, and 32 inventory items per player. Each player attempted 24 attacks in the measured interval and received 24 scripted return hits. The damage calculation could miss or evade. The separate receiver counted 15,028,265 UDP datagrams across startup, warmup and combat. ACE counted 15,028,265 sends. 0 of the 48 runs had different send and receive totals. The per-run JSON includes both counts, successful damage rolls and actual elapsed time.

The host used an AMD Ryzen 7 9800X3D, Windows x64 and .NET 8.0.31 in Release with server GC. The runtime saw four logical processors, the benchmark set no CPU affinity, and tiered compilation was disabled. Identical harness binaries ran against the four ACE.Server builds. The actors stay in place. The harness calls ACE melee damage calculation directly, scripts return hits, drains outbound UDP in a separate process and advances acknowledgements every 15 ticks. Live client latency, autonomous pathfinding, area spells and long-running shard memory require other workloads.

Twenty ACE.Server tests passed cleanly, including packet parity, retransmission, mutable-data isolation and inverse visibility tracking. The existing WorldManager startup test logged an unhandled exception in HouseManager.Tick on both the unchanged baseline and the combined build when run independently of normal server initialization. The dungeon trials completed on every build.

See the [raw trial measurements](results.json) and [reproduction instructions](README.md).
