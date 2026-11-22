# Dungeon physics under moving combat load

Two independently reversible changes were measured against fork revision `e7143abc`. The transition factory constructs its working data once. The cell registration change reuses an object’s active collision entries, drops departed entries, resets reused cell lists and preserves the newest-first collision order. [Transition fix](https://github.com/brycter/ACE/commit/2039895e703b890d6dce96c4413cb0826176757d) · [Registration fix](https://github.com/brycter/ACE/commit/6fcc956128f8287b29f58c785175e430b3ea67b2)

At 32 local fighters, the combined build used 11.6% fewer CPU seconds across the fixed workload and averaged 9.83 ms p99. At 200 moving fighters, it allocated about 255 MiB less managed memory per 1,800 ticks, while CPU seconds rose 2.4% and p99 went from 32.54 to 33.70 ms. End-of-window working set rose by about 9 MiB and 15 MiB respectively. The two-process comparisons show a repeatable allocation saving and substantial run-to-run timing variation at event size.

## Fixed workload results

Each row is the arithmetic mean of two fresh processes in opposite run orders. Every run completed 1,800 scheduled ticks with 1,024 authenticated in-world clients. CPU seconds include all threads in the server process. Elapsed seconds include pacing. Allocations are cumulative managed bytes. Heap is measured after a forced collection. Working set is the end-of-window resident memory before that collection. The p99 column averages the two per-run p99 tick-processing times. The budget is 16.67 ms at 60 Hz.

| Local fighters | Build | CPU seconds | Average CPU cores | Elapsed seconds | Allocated GiB | Heap MiB | Working set MiB | p99 ms | Ticks over budget |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 32 | Before | 11.60 | 0.387 | 30.00 | 2.295 | 675.7 | 1545.1 | 11.12 | 0.1% |
| 32 | Transition only | 10.84 | 0.361 | 30.00 | 2.203 | 675.2 | 1547.1 | 9.90 | 0.1% |
| 32 | Cell registrations only | 11.30 | 0.377 | 29.99 | 2.298 | 675.2 | 1558.2 | 11.29 | 0.1% |
| 32 | Both fixes | 10.26 | 0.342 | 29.99 | 2.191 | 675.5 | 1554.0 | 9.83 | 0.2% |
| 200 | Before | 34.65 | 1.043 | 33.22 | 10.579 | 685.3 | 1658.2 | 32.54 | 49.8% |
| 200 | Transition only | 36.41 | 1.044 | 34.88 | 10.385 | 681.6 | 1626.4 | 35.85 | 54.8% |
| 200 | Cell registrations only | 33.05 | 1.023 | 32.24 | 10.466 | 681.8 | 1743.4 | 32.60 | 47.8% |
| 200 | Both fixes | 35.48 | 1.055 | 33.62 | 10.331 | 683.1 | 1672.8 | 33.70 | 50.7% |

## Equivalent-work checks

The server resolved a doorway between Holtburg Dungeon cells `01F60289` and `01F60285` from the retail DAT geometry. Foreground clients receive staggered walk input at 5 Hz, reverse about 1.4 m either side of the opening, and attempt two scripted attacks per scheduled second. Four stock Tusker Guards return damage. Player movement and cell crossings are sampled from the authoritative server. Each sampled input reports the current server position. Correction counts include any forced-position sequence advances in the measurement window. Real network prediction and unscripted targeting can change those counts.

| Local | Build | Crossings across both runs | Meters across both runs | Force-position advances | Detached entries at end of each run | p99 each run (ms) |
|---:|---|---:|---:|---:|---|---|
| 32 | Before | 827 | 2,488 | 66 | 23, 25 | 10.06, 12.19 |
| 32 | Transition only | 821 | 2,489 | 70 | 23, 24 | 10.13, 9.68 |
| 32 | Cell registrations only | 825 | 2,494 | 47 | 0, 0 | 11.11, 11.48 |
| 32 | Both fixes | 824 | 2,489 | 64 | 0, 0 | 10.51, 9.15 |
| 200 | Before | 5,559 | 17,153 | 497 | 178, 183 | 32.34, 32.73 |
| 200 | Transition only | 5,743 | 18,011 | 506 | 167, 184 | 37.27, 34.43 |
| 200 | Cell registrations only | 5,393 | 16,665 | 460 | 0, 0 | 36.45, 28.75 |
| 200 | Both fixes | 5,622 | 17,361 | 510 | 0, 0 | 36.26, 31.13 |

All foreground fighters moved and crossed in each trial. The remaining population occupied other dungeon landblocks in groups of up to 32. A quarter attempted melee, another quarter walked and stopped, and the remainder stood in their rooms. Each character carried 32 items. Combat damage and movement inputs are scripted through server code. Loopback peers use separate sockets, authentication and acknowledgements. These measurements exclude peer CPU, native client rendering, WAN latency and full monster AI. The 30-second target overruns when the server cannot keep pace. Differences in actual elapsed time affect how far players travel.

## Retained cell registrations

The isolated tour uses the real position-commit method with synthetic resolved positions. Each of 1,024 physics objects visits 256 cells for three laps. After collection, the retained heap delta is relative to the same initialized actors after the first placement. This measures long-route registration retention separately from the two-cell event.

| Build | Live entries | Total retained entries | Retained heap change MiB | CPU seconds | Allocated MiB |
|---|---:|---:|---:|---:|---:|
| Before | 1,024 | 262,144 | 19.68 | 0.672 | 105.60 |
| Both fixes | 1,024 | 1,024 | 0.01 | 0.086 | 84.02 |

In a separate warmed loop of 50,000 calls, the compiled factory allocated 2,304 bytes per call before the fixes and 1,176 bytes afterward. Both repetitions agreed.

## ACE world scheduler

Two additional fresh processes per build ran the 200-fighter event through `WorldManager.Initialize`, which owns the world thread, housekeeping and network dispatch. The driver targets 60 batches per second and waits for each batch to complete before submitting the next. When the server falls behind, the deadline measurement accumulates that delay over the run. In the reverse-order second round, a separate timer captured the actual enqueue-to-completion time of each batch, including the queue wait and the scripted game actions. Neither timer includes a native client or an internet round trip.

| Round | Build | CPU seconds | Average CPU cores | Elapsed seconds | Allocated GiB | Working set MiB | p99 behind schedule ms | p99 enqueue to completion ms | Doorway crossings |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | Before | 34.55 | 1.046 | 33.02 | 10.520 | 1726.9 | 3373.71 | unmeasured | 2,702 |
| 1 | Both fixes | 30.75 | 1.025 | 30.01 | 10.055 | 1675.9 | 686.06 | unmeasured | 2,511 |
| 2 | Both fixes | 30.27 | 1.009 | 30.00 | 10.075 | 1686.2 | 134.66 | 28.37 | 2,543 |
| 2 | Before | 30.47 | 1.016 | 30.00 | 10.319 | 1689.1 | 127.81 | 27.00 | 2,571 |

## Reproduction

The Ryzen 7 9800X3D test host ran Windows x64 and .NET 8.0.31 with four logical processors exposed, server GC enabled and tiered compilation disabled. All 16 controlled trials used the same runner binary, dependencies and dedicated local MariaDB instance across the four server binaries. Each process warmed up for three seconds and forced a collection before measurement. The second round reversed the first round’s complete eight-run order. Both builds in each world-scheduler comparison also used identical runners. [Reproduction commands](README.md) · [Raw measurements and binary hashes](physics-results.json)

The patched build passed 24 regression tests, including four new cell-registration cases covering overlaps, collision order, children, particle emitters, replacement cells and teardown. The fixture excludes the previously failing standalone `WorldManager_Initialize` test. The scheduler comparisons initialize housekeeping and the world thread directly. A separate movement course passed teleport, stale-report handling, walking, stopping, jumping and landing checks.
