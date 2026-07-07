using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cementery.Rendering.Editor
{
    public static class VisualPerformanceReportGenerator
    {
        private const string SampleFileName = "visual-performance-samples.csv";
        private const string ReportPath = "Assets/Docs/Visual_Performance_Last_Run.md";
        private const float DesktopAverageBudgetMs = 16.67f;
        private const float DesktopP95BudgetMs = 22f;
        private const float WebAverageBudgetMs = 33.33f;
        private const float WebP95BudgetMs = 40f;

        [MenuItem("Cementery/Performance/Generate Visual Performance Report")]
        public static void GenerateReport()
        {
            string samplePath = Path.Combine(Application.persistentDataPath, SampleFileName);
            if (!File.Exists(samplePath))
            {
                Debug.LogWarning($"Visual performance sample file was not found: {samplePath}");
                return;
            }

            string[] lines = File.ReadAllLines(samplePath);
            if (lines.Length <= 1)
            {
                Debug.LogWarning($"Visual performance sample file has no data rows: {samplePath}");
                return;
            }

            Summary summary = BuildSummary(lines);
            string report = BuildReport(samplePath, summary);
            File.WriteAllText(ReportPath, report);
            AssetDatabase.ImportAsset(ReportPath);
            Debug.Log($"Visual performance report written to {ReportPath}");
        }

        private static Summary BuildSummary(string[] lines)
        {
            Summary summary = new Summary
            {
                MaxCpuFrameMs = -1f,
                MaxGpuFrameMs = -1f
            };
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                string[] columns = lines[i].Split(',');
                if (columns.Length < 10)
                    continue;

                if (!TryParse(columns[0], out float time) ||
                    !TryParse(columns[2], out float averageMs) ||
                    !TryParse(columns[3], out float p95Ms) ||
                    !TryParse(columns[4], out float worstMs) ||
                    !TryParse(columns[5], out float cpuFrameMs) ||
                    !TryParse(columns[6], out float gpuFrameMs) ||
                    !TryParse(columns[8], out float memoryMb))
                {
                    continue;
                }

                summary.SampleRows++;
                summary.LastScene = columns[1];
                summary.FirstTimestamp = summary.SampleRows == 1 ? time : summary.FirstTimestamp;
                summary.LastTimestamp = time;
                summary.AverageSumMs += averageMs;
                summary.MaxP95Ms = Mathf.Max(summary.MaxP95Ms, p95Ms);
                summary.MaxWorstMs = Mathf.Max(summary.MaxWorstMs, worstMs);
                summary.MaxCpuFrameMs = Mathf.Max(summary.MaxCpuFrameMs, cpuFrameMs);
                summary.MaxGpuFrameMs = Mathf.Max(summary.MaxGpuFrameMs, gpuFrameMs);
                summary.StartMemoryMb = summary.SampleRows == 1 ? memoryMb : summary.StartMemoryMb;
                summary.EndMemoryMb = memoryMb;
            }

            return summary;
        }

        private static string BuildReport(string samplePath, Summary summary)
        {
            StringBuilder builder = new StringBuilder(2048);
            builder.AppendLine("# Visual Performance Last Run");
            builder.AppendLine();
            builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Source CSV: `{samplePath}`");
            builder.AppendLine();

            if (summary.SampleRows == 0)
            {
                builder.AppendLine("No valid sample rows were found.");
                return builder.ToString();
            }

            float averageMs = summary.AverageSumMs / summary.SampleRows;
            float memoryDeltaMb = summary.EndMemoryMb - summary.StartMemoryMb;
            builder.AppendLine("## Summary");
            builder.AppendLine();
            builder.AppendLine("| Metric | Value |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine($"| Scene | {summary.LastScene} |");
            builder.AppendLine($"| Sample rows | {summary.SampleRows} |");
            builder.AppendLine($"| Duration | {Mathf.Max(0f, summary.LastTimestamp - summary.FirstTimestamp):F2} s |");
            builder.AppendLine($"| Average frame time | {averageMs:F2} ms |");
            builder.AppendLine($"| Worst p95 frame time | {summary.MaxP95Ms:F2} ms |");
            builder.AppendLine($"| Worst single frame | {summary.MaxWorstMs:F2} ms |");
            builder.AppendLine($"| Peak CPU frame timing | {FormatOptionalTiming(summary.MaxCpuFrameMs)} |");
            builder.AppendLine($"| Peak GPU frame timing | {FormatOptionalTiming(summary.MaxGpuFrameMs)} |");
            builder.AppendLine($"| Managed memory delta | {memoryDeltaMb:F2} MB |");
            builder.AppendLine();
            builder.AppendLine("## Budget Verdict");
            builder.AppendLine();
            builder.AppendLine("| Target | Verdict |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine($"| Desktop 60 FPS | {FormatVerdict(averageMs <= DesktopAverageBudgetMs && summary.MaxP95Ms <= DesktopP95BudgetMs)} |");
            builder.AppendLine($"| WebGL 30 FPS | {FormatVerdict(averageMs <= WebAverageBudgetMs && summary.MaxP95Ms <= WebP95BudgetMs)} |");
            builder.AppendLine();
            builder.AppendLine("## Notes");
            builder.AppendLine();
            builder.AppendLine("- Pair this report with a Unity Profiler capture for final merge evidence.");
            builder.AppendLine("- Re-run after render pipeline, cloud, fog, particle, UI animation, camera, or chunk-loading changes.");
            return builder.ToString();
        }

        private static bool TryParse(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static string FormatOptionalTiming(float value)
        {
            return value < 0f ? "Unavailable" : $"{value:F2} ms";
        }

        private static string FormatVerdict(bool pass)
        {
            return pass ? "PASS" : "NEEDS PROFILING REVIEW";
        }

        private struct Summary
        {
            public int SampleRows;
            public string LastScene;
            public float FirstTimestamp;
            public float LastTimestamp;
            public float AverageSumMs;
            public float MaxP95Ms;
            public float MaxWorstMs;
            public float MaxCpuFrameMs;
            public float MaxGpuFrameMs;
            public float StartMemoryMb;
            public float EndMemoryMb;
        }
    }
}
