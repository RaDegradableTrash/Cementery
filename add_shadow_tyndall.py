import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# 1. Insert SampleCloudShadowFast right before Fragment
shadow_function = """
            // --- FAST 2D CLOUD SHADOW PROJECTION ---
            float SampleCloudShadowFast(float3 worldPos, float3 sunDir)
            {
                // Project worldPos to the cloud layer along sunDir
                float midHeight = (_CloudMinHeight + _CloudMaxHeight) * 0.5;
                if (sunDir.y >= -0.01) return 0.0; // Sun is below horizon or horizontal
                
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
            }

            half4 Fragment(Varyings input) : SV_Target"""
            
content = re.sub(r"half4 Fragment\(Varyings input\) : SV_Target", shadow_function, content)

# 2. Modify the IntersectCloudBox check to do Ground Shadows and Tyndall
old_intersect = r"""                // Intersect ray with the cloud boundaries
                float tNear, tFar;
                if \(!IntersectCloudBox\(rayOrigin, rayDir, sceneDist, tNear, tFar\)\)
                \{
                    return half4\(0, 0, 0, 0\); // No clouds intersected or occluded by scene
                \}"""

new_intersect = """                // Intersect ray with the cloud boundaries
                float tNear, tFar;
                bool hitClouds = IntersectCloudBox(rayOrigin, rayDir, sceneDist, tNear, tFar);
                
                float4 finalColor = float4(0, 0, 0, 0);
                Light mainLight = GetMainLight();
                float jitter = InterleavedGradientNoise(input.positionCS.xy);
                
                // === GROUND SHADOWS AND TYNDALL EFFECT (GREEN FOR DEBUGGING) ===
                if (!isSkybox && sceneDist < tNear) 
                {
                    // 1. Project ground shadow
                    float groundShadow = SampleCloudShadowFast(sceneWorldPos, mainLight.direction);
                    if (groundShadow > 0.05) 
                    {
                        // Tint ground green with shadow intensity
                        finalColor.rgb += float3(0.0, 1.0, 0.0) * groundShadow * 0.8;
                        finalColor.a += groundShadow * 0.8;
                    }
                    
                    // 2. Volumetric Tyndall Light Shafts (march towards ground)
                    int tyndallSteps = 8;
                    float tyndallStepSize = sceneDist / (float)tyndallSteps;
                    float tyndallDensity = 0.0;
                    
                    // Jitter starting position
                    float tTyndall = jitter * tyndallStepSize;
                    
                    [loop]
                    for (int j = 0; j < tyndallSteps; j++)
                    {
                        float3 pos = rayOrigin + rayDir * tTyndall;
                        float shadowAtPos = SampleCloudShadowFast(pos, mainLight.direction);
                        
                        // If NOT in shadow, air is illuminated by sun (Tyndall ray!)
                        float illumination = 1.0 - shadowAtPos;
                        tyndallDensity += illumination;
                        
                        tTyndall += tyndallStepSize;
                    }
                    
                    // Average Tyndall intensity
                    float godRayIntensity = (tyndallDensity / (float)tyndallSteps);
                    // Add bright green godrays
                    if (godRayIntensity > 0.0) {
                        float phase = lerp(0.8, 2.5, saturate(dot(rayDir, mainLight.direction) * 0.5 + 0.5));
                        float3 godRayColor = float3(0.1, 1.0, 0.1) * godRayIntensity * phase * 0.6;
                        
                        finalColor.rgb += godRayColor * (1.0 - finalColor.a);
                        finalColor.a = saturate(finalColor.a + godRayIntensity * 0.6);
                    }
                }
                
                if (!hitClouds)
                {
                    return finalColor; 
                }"""

content = re.sub(old_intersect, new_intersect, content)

with open(file_path, 'w') as f:
    f.write(content)

print("Shadows and Tyndall rays applied.")
