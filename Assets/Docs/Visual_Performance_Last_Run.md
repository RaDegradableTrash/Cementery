# Visual Performance Last Run

Generated: 2026-07-08 20:11:51
Source CSV: `/Users/ra/Library/Application Support/Dustland/Cementery/visual-performance-samples.csv`
Screenshot directory: `/Users/ra/Library/Application Support/Dustland/Cementery/visual-evidence-route`
Profiler capture: `/Users/ra/Library/Application Support/Dustland/Cementery/visual-evidence-route.raw`
CSV session starts at data line: 919

## Summary

| Metric | Value |
| --- | --- |
| Scene | Main_Persistent |
| Sample rows | 12 |
| Duration | 42.52 s |
| Average frame time | 308.14 ms |
| Worst p95 frame time | 3483.89 ms |
| Worst single frame | 3483.89 ms |
| Steady-state rows | 9 after 10s warmup |
| Steady-state average frame time | 66.42 ms |
| Steady-state worst p95 frame time | 125.91 ms |
| Steady-state worst single frame | 293.89 ms |
| Peak CPU frame timing | 65.89 ms |
| Peak GPU frame timing | 54.34 ms |
| Peak GC allocated in frame | 9175.70 KB |
| Peak main thread counter | 1570.73 ms |
| Peak render thread counter | Unavailable |
| Managed memory delta | 7.47 MB |
| Time-of-day range | 0.020 to 0.480 |
| Fog enabled samples | 10 / 12 |
| Peak fog density | 0.0140 |
| Fog distance range | 8.00 to 260.00 |
| Peak ambient intensity | 1.18 |
| Distant terrain proxy active samples | 12 / 12 |
| Distant terrain proxy radius | 16384.00 m |
| Distant terrain proxy vertices | 66049 |
| Distant terrain far clip target | 12000.00 m |
| Route screenshots | 4 / 4 |
| Profiler capture size | 275.10 MB |

## Budget Verdict

| Target | Verdict |
| --- | --- |
| Desktop 60 FPS steady-state | NEEDS PROFILING REVIEW |
| WebGL 30 FPS steady-state | NEEDS PROFILING REVIEW |
| Raw startup/load spikes | NEEDS PROFILING REVIEW |
| Evidence duration | PASS |
| Day/night route coverage | PASS |
| Fog route coverage | PASS |
| Distant terrain proxy coverage | PASS |
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
