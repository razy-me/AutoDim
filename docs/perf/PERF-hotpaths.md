# Performance Report: AutoDim Frame Hotpaths & Allocations

- **Status**: COMPLETED
- **Target / Workload**: Per-frame scene update, state evaluation, Z-order occlusion partitioning, and D2D corner geometry caching (`10,000` ticks, 2 monitors, 16 windows, 4 zones)
- **Benchmark Command**: `dotnet run --project "$env:TEMP\AutoDimPerfBench\Bench.csproj" -c Release`

## Scorecard (Before vs. After)
| Metric | Baseline | After | Improvement |
|---|---|---|---|
| Latency (10,000 Core ticks) | `552.97 ms` (`55.30 µs/tick`) | `298.61 ms`–`303.75 ms` (`29.86–30.38 µs/tick`) | **`45–46% faster` (`1.82–1.85x`)** |
| Managed Allocations (10,000 Core ticks) | `195,539.5 KB` (`20,023 B/tick`) | `15,555.8 KB` (`1,593 B/tick`) | **`-92.0%` allocations** |
| Numerical Parity (`Checksum`) | `1071150378` | `1071150378` | `100% identical` |

## Optimization Ledger
- [x] **OPT-01**: Zero-allocation `DimStateEngine` scene update, config cache & LINQ removal (`src/AutoDim.Core/Engine/DimStateEngine.cs`)
  - **Bottleneck**: Re-validated `DimConfig` and allocated `HashSet`/`Dictionary`/`List`/`.ToArray()` and LINQ `.Concat`/`.Any`/`.All` enumerators on every tick.
  - **Change**: Cached `_lastRawConfig` reference, reused scratch collections, and replaced hotpath LINQ with `for`/`foreach` loops.
  - **Measured Impact**: `552.97 ms -> 354.47 ms` (`-35.9%`), `20,023 B/tick -> 9,808 B/tick` (`-51.0%`).
- [x] **OPT-02**: Scratch-buffer reuse in `OcclusionPartitioner.PartitionInto` (`src/AutoDim.Core/Engine/OcclusionPartitioner.cs`, `DimStateEngine.cs`)
  - **Bottleneck**: Allocated 3 `List` buffers per monitor per frame (`Partition`).
  - **Change**: Added `PartitionInto` with caller-owned scratch buffers (`_rectsScratch`, `_cornersScratch`, `_partRemScratch`, `_partNextScratch`).
  - **Measured Impact**: `354.47 ms -> 298.61 ms` (`-46.0%` vs baseline), `9,808 B/tick -> 1,593 B/tick` (`-92.0%` vs baseline).
- [x] **OPT-03**: Cached unit-origin D2D corner `ID2D1PathGeometry` + `WindowTracker` scratch reuse (`DirectCompositionOverlayWindow.cs`, `WindowTracker.cs`)
  - **Bottleneck**: Recreated/disposed 4+ COM `ID2D1PathGeometry` objects and allocated `HashSet`/list copies per drag/fade frame.
  - **Change**: Cached 4 unit-origin `ID2D1PathGeometry` shapes per `Radius` translated via `ctx.Transform`, reused `_lastRects`/`_lastCorners`, cached `ExcludedProcesses` `HashSet`, and reused `_activePidsScratch`/`_stalePidsScratch`.
  - **Measured Impact**: `0` per-frame COM geometry/exclusion allocations during drags and fades; all `20/20` tests & Release publish green.

## Discarded Attempts (No Measurable Gain)
- None
