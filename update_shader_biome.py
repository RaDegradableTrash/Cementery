import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/URPTriplanarEnvironment.shader"
with open(file_path, 'r') as f:
    content = f.read()

# Forward Lit Pass modification
content = content.replace("float3 normalOS : NORMAL;", "float3 normalOS : NORMAL;\n                float4 color : COLOR;")
content = content.replace("float displacement : TEXCOORD2;", "float displacement : TEXCOORD2;\n                float4 color : COLOR;")

vert_mod = """                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.color = input.color;"""
content = content.replace("""                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;""", vert_mod)

frag_mod = """                // Base triplanar texture
                float3 albedo = xAlbedo * xWeight + yAlbedo * yWeight + zAlbedo * zWeight;

                // Multiply with Biome Vertex Color (if alpha > 0.1 to avoid breaking existing terrain without colors)
                if (input.color.a > 0.05) {
                    albedo *= input.color.rgb;
                }"""
content = content.replace("""                // Base triplanar texture
                float3 albedo = xAlbedo * xWeight + yAlbedo * yWeight + zAlbedo * zWeight;""", frag_mod)

with open(file_path, 'w') as f:
    f.write(content)

print("Shader updated.")
