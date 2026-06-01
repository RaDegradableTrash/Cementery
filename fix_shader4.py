import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# 1. Restore Vertex Displacement but soft!
content = content.replace('return GetSandDeformation(posWS);', 'return GetSandDeformation(posWS) + GetSnowDisplacement(posWS);')

# 2. Lower the displacement multiplier to 0.4 so it's a "paste" instead of spikes
content = content.replace('return snowHeight * 2.5;', 'return snowHeight * 0.4;')

# 3. Increase the delta for normal calculation so it doesn't create black hard shadows
# The normal offset calculation:
# float delta = 0.15;
# float hRight = GetTotalDeformation(positionWS + float3(delta, 0, 0));
# float hUp = GetTotalDeformation(positionWS + float3(0, 0, delta));
# float3 normalOffset = float3((disp - hRight) / delta, 0.0, (disp - hUp) / delta);
# output.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS) + normalOffset * 1.5);

# Let's replace the normal calculation in ForwardLit and DepthNormals
content = re.sub(
    r'float delta = 0\.15;\s*float hRight = GetTotalDeformation.*?normalOffset \* 1\.5\);',
    r'''float delta = 0.8; // 更大的采样范围，让法线（光影）变得平滑软糯
                float hRight = GetTotalDeformation(positionWS + float3(delta, 0, 0));
                float hUp = GetTotalDeformation(positionWS + float3(0, 0, delta));
                
                float3 normalOffset = float3(
                    (disp - hRight) / delta,
                    0.0,
                    (disp - hUp) / delta
                );
                
                // 减小 offset 权重，避免出现剧烈的黑色背光面
                output.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS) + normalOffset * 0.4);''',
    content,
    flags=re.DOTALL
)

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'w') as f:
    f.write(content)

