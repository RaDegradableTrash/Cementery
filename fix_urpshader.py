import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# We need to replace the snow color blending and normal calculation in frag
# The user wants:
# float3 snowNormal = float3(0, 1, 0);
# float3 blendedNormal = lerp(input.normalWS, snowNormal, saturate(snowHeight * 2.0));
# blendedNormal = normalize(blendedNormal);
# Light mainLight = GetMainLight();
# float NdotL = saturate(dot(blendedNormal, mainLight.direction) * 0.5 + 0.5); 
# float3 snowLight = mainLight.color * NdotL * mainLight.shadowAttenuation;
# float3 snowBaseColor = float3(1.0, 0.5, 0.8);
# float3 finalSnowColor = snowBaseColor * snowLight;
# float3 result = lerp(baseFinalColor, finalSnowColor, saturate(snowHeight * 5.0));

# First, let's find the frag function
frag_start = content.find('half4 frag(')
if frag_start != -1:
    # Find the end of frag function
    frag_end = content.find('ENDHLSL', frag_start)
    frag_content = content[frag_start:frag_end]
    
    # Locate where to inject the new logic. The old logic probably has:
    # "float3 litSnow = whiteSnowColor" or something similar.
    # Let's replace everything after calculating snowHeight up to "return half4(..., 1.0);"
    
    # We'll use regex to find the snowHeight line and the return line
    snowHeight_pattern = r'(float snowHeight = SAMPLE_TEXTURE2D_LOD\(_GlobalSnowHeightMap.*?\)\.r;)'
    match = re.search(snowHeight_pattern, frag_content)
    if match:
        pre_snow = frag_content[:match.end()]
        
        # We need to find the return statement
        return_pattern = r'return\s+half4\([^\)]+\);'
        return_match = re.search(return_pattern, frag_content)
        
        if return_match:
            post_return = frag_content[return_match.end():]
            
            new_logic = """
                // 2. 积雪法线混合 (让雪堆更平滑，不产生黑色尖刺)
                float3 snowNormal = float3(0, 1, 0); // 雪层顶部的法线应该是向上的
                float3 blendedNormal = lerp(output.normalWS, snowNormal, saturate(snowHeight * 2.0));
                blendedNormal = normalize(blendedNormal);

                // 3. 计算积雪光照 (使用柔和的半兰伯特模型)
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(blendedNormal, mainLight.direction) * 0.5 + 0.5); 
                float3 snowLight = mainLight.color * NdotL * mainLight.shadowAttenuation;

                // 4. 颜色叠加 (不完全覆盖底层)
                float3 snowBaseColor = float3(1.0, 0.5, 0.8); // 粉色雪
                float3 finalSnowColor = snowBaseColor * snowLight;

                // 5. 将雪与原材质混合 (基于高度)
                float3 result = lerp(baseFinalColor, finalSnowColor, saturate(snowHeight * 5.0));

                return half4(result, 1.0);
"""
            # Wait, `baseFinalColor` and `output.normalWS` might be named differently in the shader.
            # Let's check what they are actually named.
