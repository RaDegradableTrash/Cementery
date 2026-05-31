using System;
using System.IO;

class Program
{
    static void Main()
    {
        PatchShader("/Users/ra/Documents/Cementery/Assets/Shaders/SnowBlanket.shader");
        PatchShader("/Users/ra/Documents/Cementery/Assets/Shaders/LocalSnowBlanket.shader");
    }

    static void PatchShader(string path)
    {
        string content = File.ReadAllText(path);
        
        string oldShading = @"                float litFactor = NdotL * shadowTerm;
                
                float3 shadowTint = float3(0.6, 0.75, 1.0) * 0.25; 
                float3 directLight = mainLight.color * lerp(shadowTint, float3(1.0, 1.0, 1.0), litFactor);
                
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 halfVector = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(finalNormalWS, halfVector));
                float specularIntensity = pow(NdotH, 8.0) * 0.15 * litFactor; 
                
                float3 ambient = SampleSH(finalNormalWS) * 0.2;";
                
        string newShading = @"                float litFactor = NdotL * shadowTerm;
                
                // Directly darken the light using shadow attenuation to prevent brightening
                float3 directLight = mainLight.color * litFactor;
                
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 halfVector = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(finalNormalWS, halfVector));
                float specularIntensity = pow(NdotH, 8.0) * 0.15 * litFactor; 
                
                // Add a very subtle icy blue tint to the ambient light in shadowed areas
                float3 shadowAmbient = float3(0.5, 0.65, 0.9) * 0.1;
                float3 ambient = SampleSH(finalNormalWS) * 0.05 + (1.0 - shadowTerm) * shadowAmbient;";
                
        if (content.Contains("float3 shadowTint = float3(0.6, 0.75, 1.0) * 0.25;")) {
            content = content.Replace(oldShading, newShading);
            File.WriteAllText(path, content);
            Console.WriteLine("Patched shading in " + Path.GetFileName(path));
        } else {
            Console.WriteLine("Could not find old shading in " + Path.GetFileName(path));
        }
    }
}
