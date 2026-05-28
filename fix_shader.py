import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/URPTriplanarEnvironment.shader"
with open(file_path, 'r') as f:
    content = f.read()

old_logic = """                // Multiply with Biome Vertex Color (if alpha > 0.1 to avoid breaking existing terrain without colors)
                if (input.color.a > 0.05) {
                    albedo *= input.color.rgb;
                }

                float3 baseWarmColor = _Color.rgb;
                albedo.rgb = albedo.rgb * baseWarmColor;"""

new_logic = """                // Override global base color with vertex biome color if present
                float3 baseWarmColor = input.color.a > 0.05 ? input.color.rgb : _Color.rgb;
                albedo.rgb = albedo.rgb * baseWarmColor;"""

content = content.replace(old_logic, new_logic)

with open(file_path, 'w') as f:
    f.write(content)
