using System;
using System.IO;

class Program
{
    static void Main()
    {
        string path = "/Users/ra/Documents/Cementery/Assets/Scripts/SnowParticleSystem.cs";
        string content = File.ReadAllText(path);
        
        // Use standard additive shader to ensure pure white glowing particles
        content = content.Replace(
            "Shader.Find(\"Universal Render Pipeline/Particles/Unlit\")",
            "Shader.Find(\"Mobile/Particles/Additive\")"
        );
        content = content.Replace(
            "Shader.Find(\"Particles/Standard Unlit\")",
            "Shader.Find(\"Mobile/Particles/Additive\")"
        );
        
        // High quality raycast collision ONLY against Default layer (truck) to avoid terrain max collisions and grid lines!
        content = content.Replace(
            "collision.collidesWith = ~0;",
            "collision.collidesWith = LayerMask.GetMask(\"Default\", \"Vehicle\", \"Player\", \"Water\");"
        );
        content = content.Replace(
            "collision.quality = ParticleSystemCollisionQuality.Medium;",
            "collision.quality = ParticleSystemCollisionQuality.High;"
        );
        
        // Remove terrain manual accumulation since it's now handled by the uniform shader pass!
        string toRemove = "if (SnowAccumulationManager.Instance != null)\n            {\n                SnowAccumulationManager.Instance.AddSnowAtPoint(pos, particleSnowRadius, particleSnowAmount);\n            }";
        content = content.Replace(toRemove, "// Terrain snow is now accumulated globally uniformly by SnowAccumulationManager Update!");
        
        File.WriteAllText(path, content);
        Console.WriteLine("Patched SnowParticleSystem.cs");
    }
}
