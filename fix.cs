using System;
using System.IO;

class Program
{
    static void Main()
    {
        string path = "/Users/ra/Documents/Cementery/Assets/Scripts/SnowAccumulationManager.cs";
        string content = File.ReadAllText(path);
        content = content.Replace(
            "modificationMaterial.SetVector(\"_CarParams\", new Vector4(playerCar.position.x, playerCar.position.y, playerCar.position.z, carOcclusionRadius));",
            "modificationMaterial.SetVector(\"_CarParams\", new Vector4(playerCar.position.x, playerCar.position.y, playerCar.position.z, carOcclusionRadius));\n            modificationMaterial.SetVector(\"_CarParamsForward\", new Vector4(playerCar.forward.x, playerCar.forward.y, playerCar.forward.z, 4.5f));"
        );
        File.WriteAllText(path, content);
    }
}
