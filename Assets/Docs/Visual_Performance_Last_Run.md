# Visual Performance Last Run

Generated: 2026-07-07 23:26:48
Source CSV: `/Users/ra/Library/Application Support/Dustland/Cementery/visual-performance-samples.csv`
Screenshot directory: `/Users/ra/Library/Application Support/Dustland/Cementery/visual-evidence-route`
Profiler capture: `/Users/ra/Library/Application Support/Dustland/Cementery/visual-evidence-route.raw`
CSV session starts at data line: 388

## Summary

| Metric | Value |
| --- | --- |
| Scene | Main_Persistent |
| Sample rows | 16 |
| Duration | 61.17 s |
| Average frame time | 28.02 ms |
| Worst p95 frame time | 1808.17 ms |
| Worst single frame | 2765.62 ms |
| Steady-state rows | 14 after 10s warmup |
| Steady-state average frame time | 15.10 ms |
| Steady-state worst p95 frame time | 22.45 ms |
| Steady-state worst single frame | 270.69 ms |
| Peak CPU frame timing | 66.05 ms |
| Peak GPU frame timing | 19.09 ms |
| Peak GC allocated in frame | 630.42 KB |
| Peak main thread counter | 24.78 ms |
| Peak render thread counter | Unavailable |
| Managed memory delta | 16.01 MB |
| Time-of-day range | 0.020 to 0.480 |
| Fog enabled samples | 7 / 16 |
| Peak fog density | 0.0140 |
| Fog distance range | 0.00 to 300.00 |
| Peak ambient intensity | 1.20 |
| Route screenshots | 4 / 4 |
| Profiler capture size | 619.60 MB |

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
