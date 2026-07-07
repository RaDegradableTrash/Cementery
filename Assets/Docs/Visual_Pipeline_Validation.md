# Visual Pipeline Validation

Generated: 2026-07-07 21:51:31

## Automated Checks

| Check | Verdict | Notes |
| --- | --- | --- |
| URP asset exists | PASS | Assets/Settings/URP/URP_Performance.asset |
| Renderer asset exists | PASS | Assets/Settings/URP/URP_Performance_Renderer.asset |
| Main scene uses vibrant profile | PASS | Assets/Scenes/Main_Persistent.unity |
| Vibrant profile has tone/color/bloom/vignette | PASS | Assets/New Volume Profile.asset |
| Cloud resolution scale is serialized safely | PASS | Expected Full, Half, or Quarter. |
| Cloud steps stay inside polish budget | PASS | Expected current gameplay cloud budget: 8 near steps, 3 far steps. |
| SRP batcher enabled | PASS | Assets/Settings/URP/URP_Performance.asset |
| Frame timing stats enabled | PASS | ProjectSettings/ProjectSettings.asset |
| Gameplay post-processing is camera-gated | PASS | Assets/Scripts/Rendering/VisualPipelineBootstrapper.cs |
| Sampler writes frame-time CSV | PASS | Assets/Scripts/Rendering/VisualPerformanceSampler.cs |
| Sampler records profiling counters | PASS | Assets/Scripts/Rendering/VisualPerformanceSampler.cs |
| Color space is Linear | WARN | Project is currently Gamma if this warns; migrate only after material/lighting review. |

## Verdict

PASS WITH WARNINGS: 1 warning(s). Runtime profiler evidence is still required before closing visual-performance work.

## Required Manual Evidence

- Run the checklist routes in `Assets/Docs/Visual_Performance_Checklist.md`.
- Generate `Assets/Docs/Visual_Performance_Last_Run.md` from a fresh CSV sample.
- Attach Unity Profiler CPU Timeline, Rendering, Memory, and screenshots for day, night, fog, and chunk traversal.
