using System;
using System.IO;

class Program
{
    static void Main()
    {
        string path = "/Users/ra/Documents/Cementery/Assets/Scripts/SnowAccumulationManager.cs";
        string content = File.ReadAllText(path);
        content = content.Replace(
            "Vector4 snowParams = new Vector4(mapCenter.x, mapCenter.z, mapWorldSize, 0);",
            "Vector4 snowParams = new Vector4(mapCenter.x, mapCenter.z, mapWorldSize, 1f / mapWorldSize);"
        );
        File.WriteAllText(path, content);
    }
}
