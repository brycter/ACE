# 1,024-client shard and event benchmark

Eight fresh processes ran the same build with movement authority on or off. Each run authenticated 1,024 distinct accounts over loopback UDP and put 1,024 characters into the online player registry. Timing covered 75 seconds following a three-second warmup. The session timeout was 60 seconds and the fixture cap was 1,152.

With 200 scripted characters fighting in Holtburg, the production world loop kept all 1,024 clients connected. It used 0.63 CPU cores and 1.43 GiB of resident process memory. The p99 time from a scheduled input to its completion on the world thread was 24.45 ms. In the controlled tick comparison, authority increased p99 at 32 local fighters in both runs. At 200, the authority-on p99 was 13.10 and 13.42 ms, and individual ticks reached 27.66 ms.

## Results by trial

CPU is the ACE process time across all threads divided by elapsed wall time, in equivalent busy cores. Working set is resident process memory sampled at the end of combat. The live managed heap is sampled after collection. The database and the peer process are outside these figures. The 60 Hz target allows 16.67 ms per tick. The controlled scheduler dispatches session work once per physics tick. The tick timer excludes the pacing sleep. The production scheduler dispatches session work independently and is measured separately.

| Round | Holtburg fighters | Movement authority | CPU cores | p99 tick (ms) | Worst tick (ms) | Ticks over budget / 4,500 | Working set (MiB) | Managed heap (MiB) |
|---|---|---|---:|---:|---:|---:|---:|---:|
| 1 | 32 | off | 0.28 | 7.72 | 12.52 | 0 | 1,540.3 | 678.7 |
| 1 | 32 | on | 0.36 | 10.17 | 63.72 | 3 | 1,557.2 | 676.7 |
| 1 | 200 | off | 0.55 | 13.29 | 22.30 | 27 | 1,439.3 | 685.2 |
| 1 | 200 | on | 0.56 | 13.10 | 27.66 | 27 | 1,465.4 | 680.0 |
| 2 | 32 | off | 0.27 | 7.85 | 24.31 | 1 | 1,531.6 | 677.9 |
| 2 | 32 | on | 0.31 | 9.03 | 24.47 | 2 | 1,557.9 | 677.1 |
| 2 | 200 | off | 0.56 | 11.89 | 21.16 | 21 | 1,472.2 | 681.9 |
| 2 | 200 | on | 0.58 | 13.42 | 25.56 | 26 | 1,455.1 | 675.4 |

ACE sent 20,327,993 datagrams, and the independent peers received 20,327,993 across setup, warmup and timed play. Each timed run completed 4,500 ticks. The minimum registered session count in every run was 1,024. Every session was still authenticated and world-connected at the end. The raw records include per-run acknowledgement traffic, outstanding sequence gaps, CRC errors, database connections opened, allocations and peak working set.

## ACE production world loop

One additional 75-second run per scenario used WorldManager.Initialize with movement authority enabled. ACE scheduled its own world, housing, physics and networking work. The driver submitted 60 batches of combat and movement inputs per second through the world action queue. The latency below measures from the scheduled input time until the action batch finished on the world thread. It includes producer and scheduler delay. This metric differs from processing time per physics tick and from network round-trip time.

| Local fighters | Connected throughout | CPU cores | Working set (MiB) | p99 scheduled input (ms) | Worst scheduled input (ms) | Outbound packets sent / received |
|---:|---:|---:|---:|---:|---:|---:|
| 32 | 1,024 | 0.38 | 1,541.6 | 24.16 | 27.38 | 1,592,735 / 1,592,735 |
| 200 | 1,024 | 0.63 | 1,462.8 | 24.45 | 36.82 | 3,757,552 / 3,757,552 |

## Workload

Both scenarios include four Tusker Guards in the main room. The ordinary case has 32 local fighters and 992 other players split across 31 dungeon landblocks. The event has 200 local fighters and 824 other players split across 26. One quarter of each background group attempts two attacks per second against its own Tusker, one quarter walks and stops, and the rest remain in the room. Each character has 32 items.

The host used a Ryzen 7 9800X3D, Windows x64, and .NET 8.0.31. The runtime saw four logical processors. Server GC was enabled and tiered compilation was disabled. The same four binaries and fixture database were used for all eight trials. The timed runner source is in commit b00a74db. The raw data includes the SHA-256 hashes of the four binaries. The second round ran the first round in reverse order.

Clients use distinct loopback sockets, real password authentication and connection cookies, and cumulative UDP acknowledgements. The benchmark places stock human characters into the game world after authentication and drives the normal movement game-action decoder on the server thread. It scripts damage and return hits. These results cover server work and local packet delivery. Native client prediction, internet latency, unscripted monster behavior, database save waves and longer soaks remain unqualified.

## Movement checks

With authority enabled, a stationary character retained its server position when the reported position differed. ACE advanced the forced-position sequence for the correction. A jump with zero client-specified velocity used the vertical speed computed from the character by the server. Distributed walking produced server-coordinate changes in every trial. Setup checks run outside the measured interval. A separate small course exercised a server-directed teleport into another stock dungeon, an old position report in transit, materialization, a walk of 2.28 m, key release (0.000 m of subsequent drift), and a jump that rose 0.57 m and landed.

## Database connection measurement

For the same 12-client, five-second workload, the original ACE.Database DLL opened 181 database connections and the version-caching DLL opened 15. Both used pre-existing fixture accounts and the same server and harness. This counts MariaDB’s global Connections counter from before process start through process exit, subtracting the measuring connection. It includes initialization and login. The endpoint, credentials and database for each context determine when ACE must rediscover the server version.

Twenty server regression tests were run separately. The existing WorldManager_Initialize startup test has an independently reproduced background exception on the baseline and is excluded from that count. The original four-argument benchmark entry point was also exercised.

See [raw measurements](shard-results.json) and [reproduction instructions](README.md#connected-shard-and-movement-tests).
