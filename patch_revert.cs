using System;
using System.IO;

class Program
{
    static void Main()
    {
        // 1. Remove Global Accumulation from SnowAccumulationManager
        string mgrPath = "/Users/ra/Documents/Cementery/Assets/Scripts/SnowAccumulationManager.cs";
        string mgrContent = File.ReadAllText(mgrPath);
        
        string globalBlock = @"        // Global Snow Accumulation!
        if (modificationMaterial != null && snowHeightMap != null)
        {
            modificationMaterial.SetVector(""_BrushStrength"", new Vector4(globalSnowRate, 0, 0, 0));
            RenderTexture temp = RenderTexture.GetTemporary(snowHeightMap.width, snowHeightMap.height, 0, snowHeightMap.format);
            Graphics.Blit(snowHeightMap, temp, modificationMaterial, 3);
            Graphics.Blit(temp, snowHeightMap);
            RenderTexture.ReleaseTemporary(temp);
        }";
        
        mgrContent = mgrContent.Replace(globalBlock, "");
        File.WriteAllText(mgrPath, mgrContent);
        
        // 2. Restore Terrain collision in SnowParticleSystem
        string sysPath = "/Users/ra/Documents/Cementery/Assets/Scripts/SnowParticleSystem.cs";
        string sysContent = File.ReadAllText(sysPath);
        
        sysContent = sysContent.Replace(
            "collision.collidesWith = LayerMask.GetMask(\"Default\", \"Vehicle\", \"Player\", \"Water\");",
            "collision.collidesWith = ~0;"
        );
        
        sysContent = sysContent.Replace(
            "// Terrain snow is now accumulated globally uniformly by SnowAccumulationManager Update!",
            @"if (SnowAccumulationManager.Instance != null)
            {
                SnowAccumulationManager.Instance.AddSnowAtPoint(pos, particleSnowRadius, particleSnowAmount);
            }"
        );
        
        File.WriteAllText(sysPath, sysContent);
        Console.WriteLine("Reverted to particle splat accumulation!");
    }
}
