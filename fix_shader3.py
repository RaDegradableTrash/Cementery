import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# 1. Remove GetSnowDisplacement from GetTotalDeformation so terrain doesn't spike
content = content.replace('return GetSandDeformation(posWS) + GetSnowDisplacement(posWS);', 'return GetSandDeformation(posWS);')

# 2. Fix Fragment shader snow color and AO
old_snow_logic = """                float snowAO = saturate(1.0 + (snowHeight * 2.0));
                
                float3 pinkSnowColor = float3(1.0, 0.5, 0.8);
                // Apply diffuse lighting to snow, and darken depressions using snowAO
                float3 litSnow = pinkSnowColor * (mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation * cloudShadow * NdotL + ambient);
                litSnow *= snowAO; // 增强凹陷处阴影

                float3 finalColor = lerp(baseFinalColor, litSnow, saturate(snowHeight * 5.0));"""

new_snow_logic = """                // 让雪变成柔软的白色，而不是粉色，并消除黑色阴影
                float3 whiteSnowColor = float3(0.95, 0.98, 1.0);
                
                // 让雪地稍微光滑一点，减弱法线的剧烈变化，产生软软的感觉
                float softNdotL = saturate(dot(lerp(normalWS, float3(0,1,0), 0.5), mainLight.direction) * 0.5 + 0.5);
                
                float3 litSnow = whiteSnowColor * (mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation * cloudShadow * softNdotL + ambient * 1.2);
                
                // 平滑混合
                float3 finalColor = lerp(baseFinalColor, litSnow, saturate(snowHeight * 3.0));"""

content = content.replace(old_snow_logic, new_snow_logic)

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'w') as f:
    f.write(content)

