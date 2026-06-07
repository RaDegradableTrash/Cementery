using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class PrefabInvalidScriptFixer : EditorWindow
{
    private static readonly string TargetGuid = "474bcb49853aa07438625e644c072ee6";

    [MenuItem("Tools/Fix Prefabs and Scenes (Missing URP/HDRP Lights)")]
    public static void ShowWindow()
    {
        GetWindow<PrefabInvalidScriptFixer>("Fix Missing Lights");
    }

    private void OnGUI()
    {
        GUILayout.Label("Fix Invalid Script References", EditorStyles.boldLabel);
        GUILayout.Space(10);
        GUILayout.Label("This tool strips the missing HDAdditionalLightData component references");
        GUILayout.Label("from Prefabs and Scenes to allow Prefab Overrides to work again.");
        GUILayout.Space(20);

        if (GUILayout.Button("Scan & Fix All Assets", GUILayout.Height(40)))
        {
            FixAllAssets();
        }
    }

    public static void FixAllAssets()
    {
        string assetsDir = Application.dataPath;
        string[] allFiles = Directory.GetFiles(assetsDir, "*.*", SearchOption.AllDirectories);

        List<string> filesToFix = new List<string>();
        foreach (var file in allFiles)
        {
            string ext = Path.GetExtension(file).ToLower();
            if (ext == ".prefab" || ext == ".unity")
            {
                filesToFix.Add(file);
            }
        }

        int fixedCount = 0;
        foreach (var filePath in filesToFix)
        {
            if (FixFile(filePath))
            {
                fixedCount++;
            }
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Fix Completed", $"Successfully processed {fixedCount} files and stripped missing components.", "OK");
    }

    private static bool FixFile(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        string[] lines = File.ReadAllLines(filePath);
        bool modified = false;
        List<string> outputLines = new List<string>();

        // We will parse the YAML blocks. A MonoBehaviour component block looks like:
        // --- !u!114 &ID
        // MonoBehaviour:
        //   ...
        //   m_Script: {fileID: 11500000, guid: 474bcb49853aa07438625e644c072ee6, type: 3}
        //
        // If we find a MonoBehaviour block matching the target guid, we will exclude it, 
        // AND we must strip its fileID reference from the GameObject's components list.

        Dictionary<string, string> componentToGameObject = new Dictionary<string, string>();
        Dictionary<string, List<string>> gameObjectComponents = new Dictionary<string, List<string>>();

        // Pass 1: Parse structure
        string currentBlockHeader = "";
        string currentBlockType = "";
        string currentFileID = "";
        string currentGameObjectRef = "";
        bool currentBlockIsTargetScript = false;

        List<List<string>> yamlBlocks = new List<List<string>>();
        List<string> currentBlockLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith("--- "))
            {
                if (currentBlockLines.Count > 0)
                {
                    yamlBlocks.Add(new List<string>(currentBlockLines));
                    currentBlockLines.Clear();
                }
                currentBlockHeader = line;
                currentBlockIsTargetScript = false;
                currentGameObjectRef = "";

                // Extract fileID e.g., --- !u!114 &180113071865074621
                Match m = Regex.Match(line, @"&(\d+)");
                if (m.Success)
                {
                    currentFileID = m.Groups[1].Value;
                }
                else
                {
                    currentFileID = "";
                }

                if (line.Contains("!u!114 ")) // MonoBehaviour
                {
                    currentBlockType = "MonoBehaviour";
                }
                else if (line.Contains("!u!1 ")) // GameObject
                {
                    currentBlockType = "GameObject";
                }
                else
                {
                    currentBlockType = "Other";
                }
            }

            currentBlockLines.Add(line);

            if (currentBlockType == "MonoBehaviour")
            {
                if (line.Contains(TargetGuid))
                {
                    currentBlockIsTargetScript = true;
                }
                Match m = Regex.Match(line, @"m_GameObject: \{fileID: (\d+)\}");
                if (m.Success)
                {
                    currentGameObjectRef = m.Groups[1].Value;
                    if (currentFileID != "")
                    {
                        componentToGameObject[currentFileID] = currentGameObjectRef;
                    }
                }
            }
        }
        if (currentBlockLines.Count > 0)
        {
            yamlBlocks.Add(currentBlockLines);
        }

        // Identify target fileIDs to strip
        HashSet<string> fileIDsToStrip = new HashSet<string>();
        foreach (var block in yamlBlocks)
        {
            if (block.Count == 0) continue;
            string header = block[0];
            Match m = Regex.Match(header, @"&(\d+)");
            if (m.Success && header.Contains("!u!114 "))
            {
                string fid = m.Groups[1].Value;
                bool isTarget = false;
                foreach (var l in block)
                {
                    if (l.Contains(TargetGuid))
                    {
                        isTarget = true;
                        break;
                    }
                }
                if (isTarget)
                {
                    fileIDsToStrip.Add(fid);
                }
            }
        }

        if (fileIDsToStrip.Count == 0) return false;

        // Pass 2: Reconstruct YAML, skipping target components and removing them from GameObject component list
        StringBuilder sb = new StringBuilder();
        foreach (var block in yamlBlocks)
        {
            if (block.Count == 0) continue;
            string header = block[0];
            Match m = Regex.Match(header, @"&(\d+)");
            string fid = m.Success ? m.Groups[1].Value : "";

            if (header.Contains("!u!114 ") && fileIDsToStrip.Contains(fid))
            {
                // Skip this invalid script component block entirely!
                modified = true;
                continue;
            }

            // If it is a GameObject, strip the component references
            if (header.Contains("!u!1 "))
            {
                List<string> newBlockLines = new List<string>();
                bool inComponentList = false;
                for (int i = 0; i < block.Count; i++)
                {
                    string l = block[i];
                    if (l.Contains("m_Component:"))
                    {
                        inComponentList = true;
                        newBlockLines.Add(l);
                        continue;
                    }
                    if (inComponentList)
                    {
                        // Check if it marks end of component list (indented component list member starts with '  - ')
                        if (l.StartsWith("  - ") || l.StartsWith("    - "))
                        {
                            // Parse component fileID reference
                            Match compMatch = Regex.Match(l, @"fileID: (\d+)");
                            if (compMatch.Success)
                            {
                                string compFid = compMatch.Groups[1].Value;
                                if (fileIDsToStrip.Contains(compFid))
                                {
                                    // Strip this component list element!
                                    modified = true;
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            inComponentList = false;
                        }
                    }
                    newBlockLines.Add(l);
                }
                foreach (var l in newBlockLines)
                {
                    sb.AppendLine(l);
                }
            }
            else
            {
                foreach (var l in block)
                {
                    sb.AppendLine(l);
                }
            }
        }

        if (modified)
        {
            // Write cleaned file back
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[PrefabInvalidScriptFixer] Stripped invalid components from {Path.GetFileName(filePath)}");
            return true;
        }

        return false;
    }
}
