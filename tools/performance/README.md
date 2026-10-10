# Performance measurement and regression tools

These tools compile and launch **isolated copies** of the Unity 2019.4 Win32 player. They never install a test assembly in the playable editions. Run from PowerShell on Windows; the .NET Framework C# compiler and Mono.Cecil already supplied with this repository are used.

## Full comparison

1. Capture the source preceding the optimization with `powershell -NoProfile -File tools\performance\CaptureBaseline.ps1`. Its default reference is `65b270ba388ac1c871abe6e0381dc050808258e6`.
2. Run `powershell -NoProfile -File tools\performance\RunPerformance.ps1 -Phase baseline -Edition Original`, then the same command with `-Edition Experiment`.
3. Repeat both editions with `-Phase candidate`. Avoid running other games, builds or benchmarks at the same time.

Each edition covers classic/skills mode crossed with local two-player/Fan-human versus Zhao-AI/Zhao-human versus Fan-AI. Each scene has a 20-second warmup and a 120-second measurement, repeated three times. The test clock is extended so the scene remains playable for the complete recording. Only the test build substitutes scripted key input and enables background execution for hidden test windows. The production settings, controls, physics and frame-rate policy are unchanged.

`-Smoke` uses 2-second warmups, 4-second recordings and one repetition; it verifies the harness, and is not a substitute for the complete comparison. `-Status` reads the matching isolated player's state. `-Stop` terminates only that isolated executable after checking its absolute path.

`-ContinueMatrix` waits for an already running original baseline to finish, then runs the experimental baseline, both candidates and both complete regression sets in order. It rejects unrelated running players. `-ExportResults` requires all 72 scenario summaries and exports compact evidence under `performance/`; raw per-frame CSV remains in the isolated folders. Candidate source hashes are recorded for the release gate.

Each run writes frame intervals, percentiles, slow-frame counts, physics steps, Mono heap values, GC counts, and method timings to CSV. CPU/GPU/physics engine recorders are attempted; missing samples are marked unavailable. Hidden-window VSync behavior differs from an ordinary displayed game, so these measurements compare code workload and do not certify the user's displayed FPS. Heap size is not an allocation-byte counter; GC counts must also be compared per equal frame count when throughput changes.

Method timing hooks are inserted only in a test DLL. Short IL branches are expanded before writing the instrumentation; timer stack depth is checked. Do not publish instrumented assemblies.

## Regression suites

Run `powershell -NoProfile -File tools\performance\RunRegressions.ps1 -Edition Original -Suite Performance`; repeat with `Experiment`.

The additional performance suite compares reusable physics results to `RaycastAll` (including growth, trigger policy and ordering), animation lists to the old arrays, component lifecycle behavior to the native search, and the LAN codec to `BinaryWriter`/`BinaryReader` on the game's own 32-bit Mono runtime. Snapshot comparisons include Unity's own angular-velocity rounding. It also performs three repetitions of microbenchmarks for each optimization group.

Available existing behavior suites: `Gameplay`, `Controls`, `Boundary`, `Defense`, `Goals`, `Magnetic`, `Contest`, `AI`, `Drop`, `Balance`, `Corner`, `Network`, and `UI`. Their assertions and tolerances are retained. `-CompileOnly` validates compilation without launching the player. Game launches may require normal desktop permissions for UDP sockets and rendering.

`NetworkPair` runs the identical test DLL in two separate Unity players over loopback: classic mode, movement/action acknowledgements, snapshots, scores, disconnect detection, clearing held controls, then rebuilding the room and joining in skills mode. It cannot certify the network or firewall of a second physical computer.

`-All` runs all 15 suites sequentially for the selected edition and stops on failure. A separately compiled release contains no regression components or substituted input.

Generated player directories are ignored by Git. Review the report and CSV evidence before installation, and check release assemblies for test/benchmark types.

## Quick checks requested for this update

The user shortened the scope on 2026-10-10. `RunPerformance.ps1 -Quick` runs four smoke measurements (six scenes, 2 seconds warmup + 4 seconds recording, one repetition per scene), then `Performance`, `Gameplay`, `UI` and `NetworkPair` for each edition. `-ExportResults -Smoke` exports exactly 24 summaries; `-ReportQuick` creates `PERFORMANCE.md` and microbenchmark CSV evidence. `-StopMatrix` stops only this tool's coordinator and isolated players. The cancelled full matrix is excluded from the quick report.

These short recordings are an initial workload comparison, not a demonstrated reduction of sustained gameplay stutter. The three repeated microbenchmarks measure individual optimized calls. `InstallRelease.ps1 -Quick` gates on the shortened measurements and these four selected suites, including both network roles, and still checks clean release DLLs, source hashes, installed/package consistency and exact control-file preservation.

## Validated release installation

After reviewing the completed measurements and regressions, run `powershell -NoProfile -File tools\performance\InstallRelease.ps1 -MeasurementRoot <absolute isolated measurement folder>`. It requires successful full baseline/candidate logs, 18 summaries per edition/phase, and all regression logs per edition, including both paired network roles. The current source must match the candidate hashes.

The script compiles a clean release, checks DLL types and calls for test hooks, backs up the existing runtime and sources, installs both canonical and playable editions, and builds the two complete ZIPs. Every ZIP entry is hashed against the canonical file, and all installed files are compared except `按键设置.ini`: local bindings are deliberately independent of the distributed defaults. Exact pre/post hashes confirm that all four configuration files remain untouched.

An environment interruption is recorded when a requested 120-second recording lasts over 122 seconds or contains an individual frame above 5 seconds. `-Repair` archives those original CSV/log files and repeats the affected scenario with the same seed and input schedule. If the interruption repeats, it stops instead of exporting invalid results. Ordinary slow frames, including the 33.3/50 ms thresholds, are retained. This rule applies identically to baseline and candidate.
