# Visual Performance Last Run

Generated: 2026-07-07 23:04:25
Source CSV: `/Users/ra/Library/Application Support/Dustland/Cementery/visual-performance-samples.csv`
Screenshot directory: `/Users/ra/Library/Application Support/Dustland/Cementery/visual-evidence-route`
Profiler capture: `/Users/ra/Library/Application Support/Dustland/Cementery/visual-evidence-route.raw`
CSV session starts at data line: 259

## Summary

| Metric | Value |
| --- | --- |
| Scene | Main_Persistent |
| Sample rows | 12 |
| Duration | 42.42 s |
| Average frame time | 263.35 ms |
| Worst p95 frame time | 3835.55 ms |
| Worst single frame | 3835.55 ms |
| Steady-state rows | 9 after 10s warmup |
| Steady-state average frame time | 25.85 ms |
| Steady-state worst p95 frame time | 31.11 ms |
| Steady-state worst single frame | 165.33 ms |
| Peak CPU frame timing | 100.70 ms |
| Peak GPU frame timing | 23.20 ms |
| Peak GC allocated in frame | 6714.74 KB |
| Peak main thread counter | 1000.29 ms |
| Peak render thread counter | Unavailable |
| Managed memory delta | 80.95 MB |
| Time-of-day range | 0.020 to 0.480 |
| Fog enabled samples | 9 / 12 |
| Peak fog density | 0.0140 |
| Fog distance range | 0.00 to 300.00 |
| Peak ambient intensity | 1.20 |
| Route screenshots | 4 / 4 |
| Profiler capture size | 621.40 MB |

## Budget Verdict

| Target | Verdict |
| --- | --- |
| Desktop 60 FPS steady-state | NEEDS PROFILING REVIEW |
| WebGL 30 FPS steady-state | PASS |
| Raw startup/load spikes | NEEDS PROFILING REVIEW |
| Evidence duration | PASS |
| Day/night route coverage | PASS |
| Fog route coverage | PASS |
| Screenshot route coverage | PASS |
| Profiler capture | PASS |

## Screenshot Evidence

| Screenshot | Status |
| --- | --- |
| `visual-evidence-01-day-clear.png` | FOUND |
| `visual-evidence-02-sunset-haze.png` | FOUND |
| `visual-evidence-03-night-fog.png` | FOUND |
| `visual-evidence-04-dawn-fog.png` | FOUND |

## Notes

- Pair this report with a Unity Profiler capture for final merge evidence.
- Use steady-state rows for render-pipeline cost; raw startup/load spikes remain listed separately for scene-load and chunk-streaming follow-up.
- Re-run after render pipeline, cloud, fog, particle, UI animation, camera, or chunk-loading changes.
- Review the route screenshots for day, sunset, night fog, and dawn readability before claiming visual polish complete.
- Evidence coverage checks prove only that the route sampled required day/night/fog states; screenshots and profiler captures are still required to judge readability and cost.
