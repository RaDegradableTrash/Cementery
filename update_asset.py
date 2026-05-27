import sys

asset_file = "/Users/ra/Documents/Cementery/Assets/Settings/URP/URP_Performance_Renderer.asset"
with open(asset_file, "r") as f:
    content = f.read()

import re

# We want to replace the settings block under VolumetricCloudFeature
replacements = {
    r"minHeight: .*": "minHeight: 1000",
    r"maxHeight: .*": "maxHeight: 5000",
    r"densityScale: .*": "densityScale: 0.5",
    r"threshold: .*": "threshold: 0.55",
    r"baseNoiseScale: .*": "baseNoiseScale: 0.00018",
    r"detailNoiseScale: .*": "detailNoiseScale: 0.003",
    r"detailInfluence: .*": "detailInfluence: 0.55",
    r"verticalStretch: .*": "verticalStretch: 0.51",
    r"convectiveWarp: .*": "convectiveWarp: 0.04",
    r"verticalRandomness: .*": "verticalRandomness: 0.666",
    r"puffiness: .*": "puffiness: 0.45",
    r"cloudBaseFlatness: .*": "cloudBaseFlatness: 0.9",
    r"edgeSoftness: .*": "edgeSoftness: 0.02",
    r"lightAbsorption: .*": "lightAbsorption: 0.56",
    r"backlitGlow: .*": "backlitGlow: 0.48",
    r"maxSteps: .*": "maxSteps: 10",
    r"resolutionScale: .*": "resolutionScale: 1",
    r"jitterStrength: .*": "jitterStrength: 0.2",
    r"shadowSampleDistance: .*": "shadowSampleDistance: 110",
}

for pattern, replacement in replacements.items():
    content = re.sub(pattern, replacement, content)

with open(asset_file, "w") as f:
    f.write(content)

print("Updated asset")
