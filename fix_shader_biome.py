import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/URPTriplanarEnvironment.shader"
with open(file_path, 'r') as f:
    content = f.read()

old_210 = r"float4 albedo = SampleTriplanar\(_MainTex, sampler_MainTex, input\.positionWS, blendWeights, _TriplanarScale\) \* _Color;"
new_210 = "float3 activeColor = input.color.a > 0.05 ? input.color.rgb : _Color.rgb;\n                float4 albedo = SampleTriplanar(_MainTex, sampler_MainTex, input.positionWS, blendWeights, _TriplanarScale);"
content = re.sub(old_210, new_210, content)

old_tint = r"// --- JOURNEY STYLE CLEAN SOFT GRADIENT TINT ---\s*// Blends a velvety warm sun-kissed gradient exactly like Journey's beautiful stylized look\s*float3 baseWarmColor = lerp\(float3\(0\.92, 0\.68, 0\.40\), float3\(0\.96, 0\.82, 0\.58\), NdotL\);\s*albedo\.rgb = albedo\.rgb \* baseWarmColor;"
new_tint = """// --- BIOME & JOURNEY STYLE CLEAN SOFT GRADIENT TINT ---
                // We use the Biome color as the base, and apply a soft gradient tint based on light angle
                float3 shadowTint = activeColor * 0.75;
                float3 litTint = activeColor * 1.1;
                float3 baseWarmColor = lerp(shadowTint, litTint, NdotL);
                albedo.rgb = albedo.rgb * baseWarmColor;"""
content = re.sub(old_tint, new_tint, content)

with open(file_path, 'w') as f:
    f.write(content)
