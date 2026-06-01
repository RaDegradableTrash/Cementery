import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# 1. Remove GetSnowDisplacement
content = re.sub(r'TEXTURE2D\(_GlobalSnowHeightMap\);\s*SAMPLER\(sampler_GlobalSnowHeightMap\);\s*float4 _GlobalSnowMapParams;\s*float GetSnowDisplacement\(float3 posWS\)\s*\{.*?(return 0\.0;\s*\})\s*', '', content, flags=re.DOTALL)

# 2. Fix GetTotalDeformation
content = re.sub(r'return GetSandDeformation\(posWS\)\s*\+\s*GetSnowDisplacement\(posWS\);', 'return GetSandDeformation(posWS);', content)

# 3. Remove DYNAMIC SNOW SYSTEM from frag
frag_snow_pattern = r'// --- DYNAMIC SNOW SYSTEM ---.*?float3 finalColor = lerp\(baseFinalColor, finalSnowColor, saturate\(snowHeight \* 5\.0\)\);'
content = re.sub(frag_snow_pattern, 'float3 finalColor = baseFinalColor;', content, flags=re.DOTALL)

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'w') as f:
    f.write(content)

