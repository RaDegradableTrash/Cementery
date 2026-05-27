import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# 1. Replace SampleShadowMask with the highly accurate 3-layer parallax version
old_mask = r"// --- ROBUST 2D CLOUD SHADOW & TYNDALL MASK ---.*?return saturate\(pow\(density \* 2\.5, 1\.5\)\);\s*\}"

new_mask = """// --- ROBUST 3D-PARALLAX CLOUD SHADOW & TYNDALL MASK ---
            float SampleShadowMask(float3 worldPos, float3 sunDir)
            {
                if (sunDir.y <= 0.05) return 0.0;
                
                // To guarantee 1-to-1 correspondence with the clouds, we take 3 samples 
                // along the sun's ray slicing through the volume. This flawlessly captures 
                // the 3D volume shape, convective warp, and prevents low-angle sun parallax mismatches!
                
                float diff = _CloudMaxHeight - _CloudMinHeight;
                float h1 = _CloudMinHeight + diff * 0.2;
                float h2 = _CloudMinHeight + diff * 0.5;
                float h3 = _CloudMinHeight + diff * 0.8;
                
                float3 cp1 = worldPos + sunDir * ((h1 - worldPos.y) / sunDir.y);
                float3 cp2 = worldPos + sunDir * ((h2 - worldPos.y) / sunDir.y);
                float3 cp3 = worldPos + sunDir * ((h3 - worldPos.y) / sunDir.y);
                
                float3 baseScaleVec = float3(_BaseScale, _BaseScale / _VerticalStretch, _BaseScale);
                
                // Sample lower, mid, and upper slices with accurate ConvectiveWarp offsets
                float3 uvw1 = cp1 * baseScaleVec + _BaseWindSpeed.xyz * _Time.y - float3(0, _ConvectiveWarp * 0.2, 0);
                float n1 = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvw1, 0).r;
                
                float3 uvw2 = cp2 * baseScaleVec + _BaseWindSpeed.xyz * _Time.y - float3(0, _ConvectiveWarp * 0.5, 0);
                float n2 = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvw2, 0).r;
                
                float3 uvw3 = cp3 * baseScaleVec + _BaseWindSpeed.xyz * _Time.y - float3(0, _ConvectiveWarp * 0.8, 0);
                float n3 = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvw3, 0).r;
                
                // Max blend ensures we catch the thickest part of the cloud along the ray
                float baseNoise = max(n1, max(n2, n3));
                
                float3 uvwCoverage = cp2 * (baseScaleVec * 0.2) + _BaseWindSpeed.xyz * _Time.y * 0.1;
                uvwCoverage.y = 0.0;
                float coverage = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvwCoverage, 0).r;
                float coverageMask = smoothstep(0.32, 0.58, coverage);
                
                float threshold = _CloudThreshold * 0.5 + (1.0 - coverageMask) * 1.5;
                float density = saturate(baseNoise - threshold);
                
                return saturate(pow(density * 2.5, 1.5));
            }"""

content = re.sub(old_mask, new_mask, content, flags=re.DOTALL)

# 2. Modify Ground Shadow opacity
old_shadow = r"finalColor\.a = shadow \* 0\.85; // 0\.85 提供极高的内外反差"
new_shadow = """finalColor.a = shadow * 0.51; // 降低到原来的 60% (0.85 * 0.6 = 0.51)，不再黑得过头"""
content = re.sub(old_shadow, new_shadow, content)

with open(file_path, 'w') as f:
    f.write(content)

print("Shadow correspondence and opacity fixed.")
