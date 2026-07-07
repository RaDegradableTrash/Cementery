# Visual Performance Checklist

Use this checklist before merging render pipeline, post-processing, camera, UI animation, particle, fog, day-night, or chunk-streaming visual changes.

## Budgets

| Metric | Target | Notes |
| --- | --- | --- |
| Desktop frame time | 16.67 ms average, 22 ms p95 | 60 FPS target. Capture p95 over active traversal, interaction, and vehicle movement. |
| WebGL frame time | 33.33 ms average, 40 ms p95 | 30 FPS fallback target if WebGL is used. |
| Main thread spikes | Under 8 ms over baseline | Watch scene activation, chunk load/unload, interaction highlights, UI transitions, and day-night updates. |
| Render thread/GPU spikes | Under 8 ms over baseline | Watch post-processing, transparent particles, fog, clouds, snow, outlines, and shadows. |
| Chunk load hitch | No visible input freeze | Test walking and RV driving across chunk boundaries. |
| Memory growth | No steady growth after 10 minutes | Run a loop through chunk boundaries and UI screens. |

## Required Test Routes

- `Assets/Scenes/Main_Persistent.unity`: start scene, walk, interact with objects, open/close inventory, use camera-affecting flows, and enter/exit RV.
- Chunk streaming path: cross at least four chunk boundaries on foot and in the RV.
- Night visibility path: run through day, sunset, night, and sunrise with fog/clouds enabled.
- Visual-heavy path: enable snow, clouds, outlines, UI prompts, and interaction highlights where available.

## Capture Evidence

- In the Unity Editor, run `Cementery > Performance > Validate Visual Pipeline Setup` and commit or attach `Assets/Docs/Visual_Pipeline_Validation.md` with the PR evidence.
- Unity Profiler recording with CPU Timeline, Rendering, Memory, and GC Alloc columns visible.
- In Editor Play Mode or a development build, collect `visual-performance-samples.csv` from `Application.persistentDataPath` after running the route. The sampler records average, p95, worst frame time, latest CPU/GPU frame timing when available, and managed-memory trend.
- In the Unity Editor, run `Cementery > Performance > Generate Visual Performance Report` to convert the CSV into `Assets/Docs/Visual_Performance_Last_Run.md`.
- Frame Debugger or RenderDoc pass count when render pipeline, post-processing, cloud, fog, or shadow settings change.
- Before/after screenshots for day, sunset, night, and fog/cloud-heavy states.
- Before/after build target and quality level listed in the PR.
- Note whether the test used Editor Play Mode or a player build. Player build evidence is preferred for final approval.

## Profiling Notes Template

```text
Build target:
Quality level:
Scene route:
Duration:

Average frame time:
P95 frame time:
Worst spike:
Main thread peak:
Render thread/GPU peak:
GC alloc per frame:
Memory start/end:

Pass/fail against budget:
Screenshots or profiler capture:
Follow-up issues:
```

## Merge Gates

- No new recurring GC allocation in per-frame gameplay, camera, UI, interaction, day-night, or streaming code.
- No added synchronous scene or asset load in gameplay hot paths.
- No new full-scene search in `Update`, `LateUpdate`, `FixedUpdate`, or camera transition loops.
- Post-processing must preserve readable nighttime silhouettes and interactable prompts.
- Render pipeline changes must record their expected cost and the measured before/after frame-time result.
- Any change that raises cloud, fog, particle, shadow, or bloom quality must include a matching performance measurement.

## Current Baseline Notes

- The active URP asset is `Assets/Settings/URP/URP_Performance.asset`.
- The active renderer asset is `Assets/Settings/URP/URP_Performance_Renderer.asset`.
- The reusable vibrant gameplay volume profile is `Assets/New Volume Profile.asset`.
- `Assets/Scenes/Main_Persistent.unity` now uses that reusable volume profile on its global Volume.
- `Assets/Scripts/Rendering/VisualPipelineBootstrapper.cs` enables URP post-processing only on base, world-facing gameplay cameras so inventory, storage, preview, render texture, overlay, and reflection cameras do not pay for the global profile.
- `Assets/Scripts/Rendering/VisualPerformanceSampler.cs` writes development/editor CSV samples with average, p95, worst frame time, and managed memory trend for visual PR evidence.
- `Assets/Scripts/Rendering/Editor/VisualPerformanceReportGenerator.cs` converts sampler CSV output into `Assets/Docs/Visual_Performance_Last_Run.md` for review.
- `Cementery > Performance > Validate Visual Pipeline Setup` writes `Assets/Docs/Visual_Pipeline_Validation.md` and checks the renderer asset, vibrant volume wiring, camera-gated post-processing, frame timing stats, and sampler presence before runtime profiling.
- `ProjectSettings/ProjectSettings.asset` has frame timing stats enabled so CPU/GPU frame timing can be captured where the runtime supports it.
- Known expensive visual systems to watch: custom volumetric clouds, snow accumulation, silhouette/outline rendering, fog volumes, screen shatter, and additive chunk loading.
