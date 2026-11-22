# The cost of server-authoritative movement in a crowded dungeon

With 1,024 connected clients and 32 fighters in one dungeon, the server-authoritative run completed all 1,200 ticks within 16.67 ms. At 200 fighters, authority exceeded that budget on 121 and 201 ticks in two matched runs. Default movement exceeded it on 6 and 7.

Each load trial kept 1,024 authenticated loopback clients in world. The event had 200 fighters and the smaller encounter had 32. They fought four Tuskers and repeatedly crossed a doorway in Holtburg Dungeon. Each timed window contained 1,200 input batches scheduled at 60 Hz for 20 seconds. The remaining 824 or 992 characters were distributed across other dungeons. Every foreground fighter moved and crossed in every run. The control replayed the foreground actor IDs, reported positions, and headings captured from an authoritative run. Both modes used the normal movement decoder. Background movement retained the existing scripted workload.

## Matched authority comparison

Both modes allowed up to four world workers and four outbound workers. CPU seconds cover all server threads. P99 measures tick-processing time. Meters and crossings are calculated independently from each simulated player’s server position. Event pair A used a diagnostic runner. Event pair B and the 32-fighter comparison used the recording and replay commands in this branch. Peak RSS includes fixture startup. A correction is a change in the server's force-position sequence. The counts cover the measured window across all foreground fighters.

| Fighters / pair | Movement | CPU seconds | p99 tick ms | Ticks over 16.67 ms | Peak RSS MiB | Meters traveled | Door crossings | Force-position corrections |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| 32 | Default | 8.39 | 3.69 | 0 | 1,532 | 831 | 276 | 0 |
| 32 | Authoritative | 7.20 | 4.66 | 0 | 1,542 | 832 | 280 | 18 |
| 200 A | Default | 20.31 | 13.52 | 6 | 1,670 | 5,187 | 1,675 | 0 |
| 200 A | Authoritative | 22.47 | 22.20 | 121 | 1,663 | 5,183 | 1,687 | 143 |
| 200 B | Default | 19.00 | 9.59 | 7 | 1,651 | 5,173 | 1,689 | 0 |
| 200 B | Authoritative | 24.67 | 25.05 | 201 | 1,741 | 5,180 | 1,700 | 117 |

On the two 200-fighter routes, enabling authority added 8.7 and 15.5 ms to p99. The authoritative p99 exceeded the 16.67 ms tick budget in both pairs. This fixture observed server-issued position corrections even with the scripted route. A native-client test is needed to determine how those corrections feel to players.

## Worker count and scheduler

The earlier four-processor benchmark used `WorldThreadCountMultiplier = 0.34`. ACE truncates the product, so the resulting limits were one world worker and one outbound worker. A value of `1.0` raised both to four in this fixture. Both landblock group multithreading switches were enabled in every fixture configuration and the database multiplier was 0.66. The outbound-only rows used a diagnostic override to isolate session dispatch. They share the production server DLL with the ordinary settings.

| Authoritative workload | World / outbound workers | CPU seconds | p99 tick ms | Ticks over 16.67 ms | Meters traveled |
|---|---|---:|---:|---:|---:|
| Original settings | 1 / 1 | 27.34 | 37.47 | 764 | 6,714 |
| Original settings, repeat | 1 / 1 | 20.02 | 28.47 | 505 | 5,179 |
| Outbound only | 1 / 2 | 21.53 | 24.74 | 249 | 5,183 |
| Outbound only | 1 / 4 | 22.75 | 22.61 | 122 | 5,187 |
| Outbound only, repeat | 1 / 4 | 22.14 | 23.02 | 144 | 5,187 |
| Shared setting | 4 / 4 | 22.47 | 22.20 | 121 | 5,183 |
| Shared setting, repeat | 4 / 4 | 22.19 | 19.66 | 97 | 5,188 |
| Shared setting, verification | 4 / 4 | 24.67 | 25.05 | 201 | 5,180 |

ACE’s actual world scheduler was also exercised once per setting. Enqueue-to-completion measures the delay after an input batch is submitted to the world thread. Time behind schedule also includes delay accumulated by the serial input driver.

| World / outbound workers | CPU seconds | Actual elapsed seconds | p99 enqueue to completion ms | p99 behind schedule ms | Meters traveled |
|---|---:|---:|---:|---:|---:|
| 1 / 1 | 20.47 | 20.01 | 30.48 | 177.15 | 5,169 |
| 4 / 4 | 23.80 | 20.00 | 17.92 | 31.14 | 5,230 |

## Where the time went

A 12-second .NET sampling trace covered the warmed, one-worker authoritative run. These are inclusive shares of the sampled main benchmark loop. Nested entries overlap. Socket calls can include time spent in the kernel. Idle background threads are excluded from the denominator.

| Work on the measured loop | Sampled share |
|---|---:|
| outbound/session work | 57.6% |
| UDP send | 43.2% |
| physics tick | 35.1% |
| cell collision candidate checks | 22.4% |
| target PK status | 8.7% |

A trial shortcut tested horizontal separation before the usual collision work for upright two-sphere player models. Its four-worker p99s were 24.29 ms, 27.38 ms. Contemporaneous control runs measured 22.20 and 19.66 ms, so the prototype was removed. All load measurements used the same server binary except the three discarded prototype trials identified in the raw results.

## Scope

The fixture uses loopback authentication and UDP delivery. The driver invokes ACE movement game-action handlers in the server process. Melee damage is scripted. The test covers a concentrated event on one Windows host and short windows. Internet latency, native client movement prediction, and a live shard with autonomous monster AI require separate qualification. Authority runs checked that a discrepant position report was corrected and that the server supplied jump velocity. A separate 12-client course verified a server-directed teleport, rejection of an old position after transfer, walking, zero drift after key release, and a jump and landing. The recorded jump was 0.57 m high.

Use the documented commands to repeat the matched comparison with your own fixture. [Reproduction commands](README.md) · [Individual measurements, trace digest, and binary hashes](authority-results.json)
