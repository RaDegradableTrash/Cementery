# Sea-Level Ocean System

Issue #52 adds a runtime ocean surface around y=0 without editing baked terrain chunk scenes.

## Runtime Behavior

- `SeaLevelOceanSystem` bootstraps automatically after scene load and persists across scene transitions.
- The ocean follows the same target priority as world streaming: `WorldStreamer.trackingTarget`, then the player, then the main camera.
- A fixed pool of chunk-sized tiles is repositioned around the tracked target. Default coverage is a 5x5 tile grid.
- Tile size follows the larger of `WorldStreamer.chunkSizeX` and `WorldStreamer.chunkSizeZ`, falling back to 256 meters.
- Wave motion, shimmer, crest foam, and shoreline/contact foam run in `Environment/URPSeaLevelOcean` on the GPU. The CPU does not rebuild water vertices every frame.

## Tuning

Default values are set on `SeaLevelOceanSystem`:

| Setting | Default | Notes |
| --- | ---: | --- |
| `seaLevel` | `0` | Still-water baseline in world y. |
| `tileRadius` | `2` | Creates 25 pooled tiles around the target. Increase only after profiling. |
| `fallbackTileSize` | `256` | Used until `WorldStreamer` exposes chunk size. |
| `tileResolution` | `24` | Mesh density per tile. Shader animation makes this visible without high CPU cost. |
| `waveAmplitude` | `0.55` | Visible water height variation around y=0. |
| `waveSpeed` | `0.65` | Gentle wave motion for third-person player and RV cameras. |
| `primaryWaveLength` | `62` | Broad swell size. |
| `secondaryWaveLength` | `24` | Smaller surface movement and shimmer variation. |
| `shimmerStrength` | `1.2` | Specular highlight intensity from camera and main light direction. |
| `specularPower` | `96` | Higher values create tighter highlights. |
| `alpha` | `0.72` | Overall transparency. |
| `foamColor` | `(0.92, 0.98, 1.0, 0.88)` | White-blue foam tint blended over the water color. |
| `foamIntensity` | `1.35` | Overall crest and shoreline foam strength. |
| `crestFoamThreshold` | `0.68` | Higher values restrict foam to sharper wave peaks. |
| `shorelineFoamDepth` | `2.4` | Screen-depth intersection width for terrain/object contact foam. |
| `foamNoiseScale` | `0.045` | Breaks up foam so it does not appear as a static outline. |

## Performance Budget

- Default tile count is 25.
- Default tile mesh resolution is 24x24 quads, or 625 vertices per tile.
- Total default ocean geometry is about 15,625 vertices, shared through one mesh and one runtime material.
- No additive scenes are loaded for water, so chunk streaming load and activation queues are not increased.
- No per-frame CPU wave mesh rebuild is used. Runtime CPU work is limited to target refresh, occasional chunk-size refresh, tile repositioning when the target changes grid cells, and material property updates.
- Shoreline foam depends on the URP camera depth texture. If contact foam is missing in a quality tier, enable depth texture support on the active URP asset or gameplay camera.

Use the existing visual performance sampler or Unity Profiler before increasing `tileRadius` or `tileResolution`.
