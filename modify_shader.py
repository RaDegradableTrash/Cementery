import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# 1. We need to define _GlobalSnowHeightMap in passes 2, 3, 4
sampler_def = """
            TEXTURE2D(_GlobalSnowHeightMap);
            SAMPLER(sampler_GlobalSnowHeightMap);
            float4 _GlobalSnowMapParams;
"""

# Replace in DepthOnly pass
content = re.sub(
    r'(CBUFFER_START\(UnityPerMaterial\).*?CBUFFER_END)',
    r'\1\n' + sampler_def,
    content,
    flags=re.DOTALL
)

# Wait, the first pass ALREADY has it before CBUFFER_START.
# My regex above will add it AFTER CBUFFER_END in all passes!
# But ForwardLit already has it BEFORE CBUFFER_START. Let's clean up ForwardLit so it's consistent?
# No, let's just make the regex smarter. We only want to add it where it's missing.

