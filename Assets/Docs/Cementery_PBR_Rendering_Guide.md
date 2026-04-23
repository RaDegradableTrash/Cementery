# Cementery PBR Rendering Guide

This project uses a PBR-oriented lighting workflow with built-in fallback and HDRP Physically Based Sky support.

## 1. Core Rules

- Keep color space in Linear.
- Keep one physically plausible sun as the primary directional light.
- Use Light Probes for dynamic objects crossing indoor/outdoor or shadow boundaries.
- Use Reflection Probes for stable metal/specular behavior in shadow.
- Do not paint baked directional highlights into Albedo textures.

## 2. Material Channel Rules

- Albedo: Base color only. No baked AO, no baked direct shadows.
- Metallic: Prefer binary values (0 dielectrics, 1 metals), with transitions for rust/dirt.
- Roughness/Smoothness: Use texture variation, not flat constants.
- Normal: Use tangent-space normals from a correct DCC workflow.
- AO: Use AO map for creases/contacts and combine with scene AO where available.

## 3. Scene Setup Checklist

1. Place at least one ReflectionProbe in gameplay spaces.
2. Place LightProbeGroup points around shadow boundaries, doors, tunnels, and under overhangs.
3. Ensure dynamic actors (player, carried objects, enemies) use Blend Probes.
4. Keep shadow quality settings stable (distance, cascades, bias) and avoid extreme bias values.

## 4. Runtime Controller

The script Assets/Scripts/DayNightSkyboxController.cs now:

- Controls sun rotation/intensity/color over day-night cycle.
- Applies physically consistent ambient (Trilight by default).
- Maintains reflection intensity and optional DynamicGI environment refresh.
- Auto-enforces Blend Probes on dynamic renderers.
- Runs PBR setup validation checks at startup.
- Supports HDRP Physically Based Sky when HDRP pipeline is active.

## 4.1 HDRP Physically Based Sky Migration

1. Install HDRP package (com.unity.render-pipelines.high-definition) and wait for Unity package import.
2. In Unity, create an HDRP Pipeline Asset and assign it to Project Settings > Graphics > Scriptable Render Pipeline Settings.
3. Let HDRP wizard run material and project setup fixes.
4. Keep DayNightSkyboxController.preferHdrpPhysicallyBasedSky enabled.
5. Assign a global Volume to DayNightSkyboxController.hdrpSkyVolume (or allow runtime auto-create).
6. Ensure that profile contains VisualEnvironment with sky type set to Physically Based and a PhysicallyBasedSky override.

Notes:
- In HDRP mode, built-in procedural skybox, RenderSettings ambient, and built-in reflection intensity controls are bypassed.
- Sun rotation/intensity/day-night timing from DayNightSkyboxController still applies.

## 5. Validation Warnings

At startup or via context menu "Validate PBR Setup Now", warnings are produced for:

- Non-Linear color space.
- Missing ReflectionProbe.
- Missing LightProbeGroup.
- Suspicious albedo luminance values outside configured sRGB bounds.

## 6. Recommended Authoring Ranges

- Dark albedo lower bound: ~30 sRGB.
- Bright albedo upper bound: ~240 sRGB.
- Concrete: low metallic, high roughness.
- Wet or polished metal: high metallic, lower roughness.

## 7. Notes For This Project

- Previous non-physical shadow override logic was removed.
- Lighting now relies on standard lit shading, probes, reflections, and shadow settings.
- If a dynamic object still shades incorrectly in shadow transitions, increase LightProbeGroup density in that area first.
