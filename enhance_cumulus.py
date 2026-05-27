import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# Enhance the detail carving to create strong cauliflower shapes instead of flat ellipses
pattern_detail = r"float boundaryWarp = detailNoise \* \(_Puffiness \* detailFade\) \* 0\.15 \* heightFactor \* \(1\.0 - baseShape\);\s*float edgeCarving = \(1\.0 - detailNoise\) \* \(_DetailInfluence \* detailFade\) \* 0\.12 \* \(1\.0 - baseShape\);\s*float finalShape = cloudVal \+ boundaryWarp - edgeCarving;"

replacement_detail = r"""
                // --- 8. INTENSE CAULIFLOWER CARVING & POPCORN STRUCTURE ---
                // Increase erosion multiplier to carve deep, structured canyons into the smooth ellipse
                float erosionModifier = lerp(0.2, 1.2, heightFactor); // Carve more at the fluffy tops, less at the flat bottoms
                float edgeCarving = (1.0 - detailNoise) * (_DetailInfluence * detailFade) * erosionModifier * 0.7;
                
                // Subtract carving to create distinct structured clumps
                float carvedShape = cloudVal - edgeCarving;
                
                // Add popcorn bulging to make the clumps spherical and volumetric
                float boundaryWarp = detailNoise * (_Puffiness * detailFade) * 0.4 * saturate(carvedShape * 2.0);
                
                float finalShape = carvedShape + boundaryWarp;
"""

content = re.sub(pattern_detail, replacement_detail, content)

with open(file_path, 'w') as f:
    f.write(content)

print("Cumulus detail enhancement applied.")
