import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

snow_func = """
            TEXTURE2D(_GlobalSnowHeightMap);
            SAMPLER(sampler_GlobalSnowHeightMap);
            float4 _GlobalSnowMapParams;

            float GetSnowDisplacement(float3 posWS)
            {
                float2 snowUV = (posWS.xz - _GlobalSnowMapParams.xy) * _GlobalSnowMapParams.w + 0.5;
                if (snowUV.x >= 0.0 && snowUV.x <= 1.0 && snowUV.y >= 0.0 && snowUV.y <= 1.0)
                {
                    float snowHeight = SAMPLE_TEXTURE2D_LOD(_GlobalSnowHeightMap, sampler_GlobalSnowHeightMap, snowUV, 0).r;
                    return snowHeight * 2.5; // 3D 雪堆隆起高度
                }
                return 0.0;
            }

            float GetTotalDeformation(float3 posWS)
            {
                return GetSandDeformation(posWS) + GetSnowDisplacement(posWS);
            }
"""

# 1. Remove the old global variables from ForwardLit pass
content = re.sub(r'TEXTURE2D\(_GlobalSnowHeightMap\);\s*SAMPLER\(sampler_GlobalSnowHeightMap\);\s*float4 _GlobalSnowMapParams;', '', content)

# 2. Inject the new functions right before GetSandDeformation
content = content.replace('float GetSandDeformation(float3 posWS)', snow_func + '\n            float GetSandDeformation(float3 posWS)')

# 3. Replace calls to GetSandDeformation with GetTotalDeformation where it matters
content = content.replace('GetSandDeformation(positionWS', 'GetTotalDeformation(positionWS')

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'w') as f:
    f.write(content)

