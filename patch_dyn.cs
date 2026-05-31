using System;
using System.IO;

class Program
{
    static void Main()
    {
        string path = "/Users/ra/Documents/Cementery/Assets/Scripts/DynamicSnowObject.cs";
        string content = File.ReadAllText(path);
        
        // Replace bounds calculation with a safe hardcoded 12x12x12 volume for the truck
        int start = content.IndexOf("Vector3 min = new Vector3(");
        int end = content.IndexOf("localBounds.Expand(0.5f);") + "localBounds.Expand(0.5f);".Length;
        
        if (start != -1 && end != -1) {
            string toReplace = content.Substring(start, end - start);
            content = content.Replace(toReplace, "localBounds = new Bounds(Vector3.zero, new Vector3(12f, 12f, 12f));");
            File.WriteAllText(path, content);
            Console.WriteLine("Patched DynamicSnowObject.cs successfully!");
        } else {
            Console.WriteLine("Could not find bounds calculation.");
        }
    }
}
