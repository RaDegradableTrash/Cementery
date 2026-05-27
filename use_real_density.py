import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

old_func = """            // --- FAST 2D CLOUD SHADOW PROJECTION ---
            float SampleCloudShadowFast(float3 worldPos, float3 sunDir)
            {
                // Project worldPos to the cloud layer along sunDir
                float midHeight = (_CloudMinHeight + _CloudMaxHeight) * 0.5;
                if (sunDir.y <= 0.01) return 0.0; // Sun is below horizon
                
                float distToCloud = (midHeight - worldPos.y) / sunDir.y;
                if (distToCloud < 0.0) return 0.0; // We are above the clouds looking down
                
                float3 cloudPos = worldPos + sunDir * distToCloud;
                
                float3 baseScaleVec = float3(_BaseScale, _BaseScale / _VerticalStretch, _BaseScale);
                float3 uvwBase = cloudPos * baseScaleVec + _BaseWindSpeed.xyz * _Time.y;
                float baseNoise = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvwBase, 0).r;
                
                float3 uvwCoverage = cloudPos * (baseScaleVec * 0.2) + _BaseWindSpeed.xyz * _Time.y * 0.1;
                uvwCoverage.y = 0.0;
                float coverage = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvwCoverage, 0).r;
                float coverageMask = smoothstep(0.32, 0.58, coverage);
                
                float distToCam = length(cloudPos - _WorldSpaceCameraPos);
                float distRatio = saturate(distToCam / _MaxRenderDist);
                float localThreshold = _CloudThreshold * 0.5 + (1.0 - coverageMask) * 1.5 + distRatio * 0.25;
                
                float cloudVal = baseNoise - localThreshold;
                
                // Return intense shadow factor
                return saturate(cloudVal * 6.0);
            }"""

new_func = """            // --- FAST 2D CLOUD SHADOW PROJECTION ---
            float SampleCloudShadowFast(float3 worldPos, float3 sunDir)
            {
                // Project worldPos to the cloud layer along sunDir
                float midHeight = (_CloudMinHeight + _CloudMaxHeight) * 0.5;
                if (sunDir.y <= 0.01) return 0.0; // Sun is below horizon
                
                float distToCloud = (midHeight - worldPos.y) / sunDir.y;
                if (distToCloud < 0.0) return 0.0; // We are above the clouds looking down
                
                float3 cloudPos = worldPos + sunDir * distToCloud;
                
                // Use the EXACT same function used to render the clouds to guarantee perfect shadow shape matching!
                float density = SampleCloudDensity(cloudPos);
                
                // Return intense shadow factor (if density > 0.01, it casts a hard shadow)
                return saturate(density * 10.0);
            }"""

content = content.replace(old_func, new_func)

with open(file_path, 'w') as f:
    f.write(content)

print("Swapped to SampleCloudDensity.")
