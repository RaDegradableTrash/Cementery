using System;
using System.IO;

class Program
{
    static void Main()
    {
        string path = "/Users/ra/Documents/Cementery/Assets/Scripts/SnowAccumulationManager.cs";
        string content = File.ReadAllText(path);
        
        // Add globalSnowRate
        content = content.Replace(
            "public float carOcclusionRadius = 3.5f;",
            "public float carOcclusionRadius = 3.5f;\n    public float globalSnowRate = 0.05f;"
        );
        
        // Add Pass 3 to SnowModification.shader
        string shaderPath = "/Users/ra/Documents/Cementery/Assets/Shaders/SnowModification.shader";
        string shaderContent = File.ReadAllText(shaderPath);
        if (!shaderContent.Contains("Pass 3: Global Accumulation")) {
            shaderContent = shaderContent.Replace(
                "ENDCG\n        }\n    }\n}",
                "ENDCG\n        }\n        Pass { // Pass 3: Global Accumulation\n            CGPROGRAM\n            #pragma vertex vert\n            #pragma fragment frag\n            #include \"UnityCG.cginc\"\n            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };\n            v2f vert(appdata_base v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }\n            sampler2D _MainTex;\n            sampler2D _OcclusionMap;\n            float4 _BrushStrength;\n            fixed4 frag (v2f i) : SV_Target {\n                float current = tex2D(_MainTex, i.uv).r;\n                float occlusion = tex2D(_OcclusionMap, i.uv).r;\n                return saturate(current + _BrushStrength.x * occlusion * unity_DeltaTime.x);\n            }\n            ENDCG\n        }\n    }\n}"
            );
            File.WriteAllText(shaderPath, shaderContent);
            Console.WriteLine("Patched SnowModification.shader");
        }
        
        // Update() to call Pass 3
        if (!content.Contains("Pass 3")) {
            content = content.Replace(
                "UpdateGlobalShaderParams();\n    }",
                "// Global Snow Accumulation!\n        modificationMaterial.SetVector(\"_BrushStrength\", new Vector4(globalSnowRate, 0, 0, 0));\n        RenderTexture temp = RenderTexture.GetTemporary(snowHeightMap.width, snowHeightMap.height, 0, snowHeightMap.format);\n        Graphics.Blit(snowHeightMap, temp, modificationMaterial, 3);\n        Graphics.Blit(temp, snowHeightMap);\n        RenderTexture.ReleaseTemporary(temp);\n\n        UpdateGlobalShaderParams();\n    }"
            );
            File.WriteAllText(path, content);
            Console.WriteLine("Patched SnowAccumulationManager.cs");
        }
    }
}
