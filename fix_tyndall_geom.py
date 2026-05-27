import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

old_tyndall = """                        // Check geometry shadow for Tyndall ray
                        float4 rayShadowCoord = TransformWorldToShadowCoord(pos);
                        float rayGeomAtten = MainLightRealtimeShadow(rayShadowCoord);
                        illumination *= rayGeomAtten; // God rays shouldn't appear inside truck shadows!"""

new_tyndall = """                        // Check geometry shadow for Tyndall ray ONLY if it's close to the camera (within shadow cascade bounds)
                        // This prevents out-of-bounds shadow map sampling which incorrectly returns 0 (shadowed) for the entire sky!
                        if (t < 300.0)
                        {
                            float4 rayShadowCoord = TransformWorldToShadowCoord(pos);
                            float rayGeomAtten = MainLightRealtimeShadow(rayShadowCoord);
                            illumination *= rayGeomAtten; // God rays shouldn't appear inside truck shadows!
                        }"""

content = content.replace(old_tyndall, new_tyndall)

with open(file_path, 'w') as f:
    f.write(content)

print("Tyndall geom fixed.")
