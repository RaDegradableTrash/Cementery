using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EnvironmentSystem.Editor
{
    [InitializeOnLoad]
    public static class AuraDefineSymbolAutoInjector
    {
        private const string Symbol = "AURA_2_PRESENT";

        static AuraDefineSymbolAutoInjector()
        {
            // Run automatic detection on startup/compilation
            CheckAndInjectSymbol();
        }

        [MenuItem("Tools/Environment/Check for Aura 2 Presence")]
        public static void CheckAndInjectSymbol()
        {
            bool hasAura2 = DetectAura2();
            SetDefineSymbol(Symbol, hasAura2);
        }

        private static bool DetectAura2()
        {
            // Search through loaded assemblies for Aura2 API classes
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    // Aura 2 typical main component namespace/name
                    var type = assembly.GetType("Aura2API.Aura") ?? 
                               assembly.GetType("Aura2API.AuraCamera") ??
                               assembly.GetType("Aura2API.AuraVolume");
                    if (type != null)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore loading errors for specific system/compiled assemblies
                }
            }
            return false;
        }

        private static void SetDefineSymbol(string symbol, bool enabled)
        {
            BuildTargetGroup buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (buildTargetGroup == BuildTargetGroup.Unknown)
                return;

            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            string[] symbols = defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(s => s.Trim())
                                      .ToArray();

            bool hasSymbol = symbols.Contains(symbol);

            if (enabled && !hasSymbol)
            {
                var newSymbols = symbols.ToList();
                newSymbols.Add(symbol);
                string newDefines = string.Join(";", newSymbols);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, newDefines);
                Debug.Log($"<b>[AuraFogSystem]</b> Auto-detected Aura 2. Injected '{symbol}' define symbol.");
            }
            else if (!enabled && hasSymbol)
            {
                var newSymbols = symbols.Where(s => s != symbol).ToList();
                string newDefines = string.Join(";", newSymbols);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, newDefines);
                Debug.Log($"<b>[AuraFogSystem]</b> Aura 2 not detected in assemblies. Removed '{symbol}' define symbol.");
            }
        }
    }
}
