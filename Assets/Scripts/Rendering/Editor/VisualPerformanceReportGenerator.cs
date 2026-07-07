using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cementery.Rendering.Editor
{
    [InitializeOnLoad]
    public static class VisualPerformanceReportGenerator
    {
        private const string SampleFileName = "visual-performance-samples.csv";
        private const string ReportPath = "Assets/Docs/Visual_Performance_Last_Run.md";
        private const string ValidationReportPath = "Assets/Docs/Visual_Pipeline_Validation.md";
        private const string ScreenshotDirectoryName = "visual-evidence-route";
        private const string PendingEvidenceRouteKey = "Cementery.VisualEvidenceRoute.Pending";
        private const string RunningEvidenceRouteKey = "Cementery.VisualEvidenceRoute.Running";
        private const string UrpAssetPath = "Assets/Settings/URP/URP_Performance.asset";
        private const string RendererAssetPath = "Assets/Settings/URP/URP_Performance_Renderer.asset";
        private const string MainScenePath = "Assets/Scenes/Main_Persistent.unity";
        private const string VolumeProfilePath = "Assets/New Volume Profile.asset";
        private const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
        private const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";
        private const string BootstrapperPath = "Assets/Scripts/Rendering/VisualPipelineBootstrapper.cs";
        private const string SamplerPath = "Assets/Scripts/Rendering/VisualPerformanceSampler.cs";
        private const string EvidenceRouteDriverPath = "Assets/Scripts/Rendering/VisualEvidenceRouteDriver.cs";
        private const string ReportGeneratorPath = "Assets/Scripts/Rendering/Editor/VisualPerformanceReportGenerator.cs";
        private const string VibrantVolumeGuid = "cc75cfaad567e424a8a59c3fc3927bbc";
        private const float DesktopAverageBudgetMs = 16.67f;
        private const float DesktopP95BudgetMs = 22f;
        private const float WebAverageBudgetMs = 33.33f;
        private const float WebP95BudgetMs = 40f;
        private const float MinimumEvidenceDurationSeconds = 30f;

        static VisualPerformanceReportGenerator()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

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

        [MenuItem("Cementery/Performance/Validate Visual Pipeline Setup")]
        public static void ValidateVisualPipelineSetup()
        {
            string report = BuildValidationReport();
            File.WriteAllText(ValidationReportPath, report);
            AssetDatabase.ImportAsset(ValidationReportPath);
            Debug.Log($"Visual pipeline validation report written to {ValidationReportPath}");
        }

        [MenuItem("Cementery/Performance/Start Visual Evidence Route")]
        public static void StartVisualEvidenceRoute()
        {
            if (!Application.isPlaying)
            {
                SessionState.SetBool(PendingEvidenceRouteKey, true);
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorApplication.isPlaying = true;

                Debug.Log("Visual evidence route queued. Unity will start it after entering Play Mode.");
                return;
            }

            VisualEvidenceRouteDriver existing = UnityEngine.Object.FindFirstObjectByType<VisualEvidenceRouteDriver>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeObject = existing.gameObject;
                Debug.Log("Visual evidence route is already running.");
                return;
            }

            GameObject go = new GameObject("VisualEvidenceRouteDriver");
            go.AddComponent<VisualEvidenceRouteDriver>();
            SessionState.SetBool(RunningEvidenceRouteKey, true);
            Selection.activeObject = go;
            Debug.Log("Visual evidence route started. Keep Play Mode running until the route completion log appears; the visual performance report will be generated automatically.");
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingEvidenceRouteKey, false))
                return;

            SessionState.SetBool(PendingEvidenceRouteKey, false);
            EditorApplication.delayCall += StartVisualEvidenceRoute;
        }

        private static void OnEditorUpdate()
        {
            if (!SessionState.GetBool(RunningEvidenceRouteKey, false))
                return;

            if (!Application.isPlaying)
            {
                SessionState.SetBool(RunningEvidenceRouteKey, false);
                return;
            }

            VisualEvidenceRouteDriver existing = UnityEngine.Object.FindFirstObjectByType<VisualEvidenceRouteDriver>(FindObjectsInactive.Include);
            if (existing != null)
                return;

            SessionState.SetBool(RunningEvidenceRouteKey, false);
            GenerateReport();
            Debug.Log("Visual evidence route finished; visual performance report generation was requested.");
        }

        private static Summary BuildSummary(string[] lines)
        {
            Summary summary = new Summary
            {
                MaxCpuFrameMs = -1f,
                MaxGpuFrameMs = -1f,
                MaxGcAllocatedKb = -1f,
                MaxMainThreadMs = -1f,
                MaxRenderThreadMs = -1f,
                MinTimeOfDay = 2f,
                MaxTimeOfDay = -1f,
                MaxFogDensity = -1f,
                MinFogStartDistance = float.MaxValue,
                MaxFogEndDistance = -1f,
                MaxAmbientIntensity = -1f
            };
            int startLine = FindLatestHeaderLine(lines) + 1;
            summary.SessionStartLine = startLine + 1;
            for (int i = startLine; i < lines.Length; i++)
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

                if (TryParseOptional(columns, 10, out float gcAllocatedKb))
                    summary.MaxGcAllocatedKb = Mathf.Max(summary.MaxGcAllocatedKb, gcAllocatedKb);

                if (TryParseOptional(columns, 11, out float mainThreadMs))
                    summary.MaxMainThreadMs = Mathf.Max(summary.MaxMainThreadMs, mainThreadMs);

                if (TryParseOptional(columns, 12, out float renderThreadMs))
                    summary.MaxRenderThreadMs = Mathf.Max(summary.MaxRenderThreadMs, renderThreadMs);

                if (TryParseOptional(columns, 13, out float timeOfDay))
                {
                    summary.VisualStateRows++;
                    summary.MinTimeOfDay = Mathf.Min(summary.MinTimeOfDay, timeOfDay);
                    summary.MaxTimeOfDay = Mathf.Max(summary.MaxTimeOfDay, timeOfDay);
                }

                if (TryParseOptional(columns, 14, out float fogEnabled) && fogEnabled > 0.5f)
                    summary.FogEnabledRows++;

                if (TryParseOptional(columns, 15, out float fogDensity))
                    summary.MaxFogDensity = Mathf.Max(summary.MaxFogDensity, fogDensity);

                if (TryParseOptional(columns, 16, out float fogStartDistance))
                    summary.MinFogStartDistance = Mathf.Min(summary.MinFogStartDistance, fogStartDistance);

                if (TryParseOptional(columns, 17, out float fogEndDistance))
                    summary.MaxFogEndDistance = Mathf.Max(summary.MaxFogEndDistance, fogEndDistance);

                if (TryParseOptional(columns, 18, out float ambientIntensity))
                    summary.MaxAmbientIntensity = Mathf.Max(summary.MaxAmbientIntensity, ambientIntensity);
            }

            return summary;
        }

        private static int FindLatestHeaderLine(string[] lines)
        {
            int headerLine = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("time,scene,", StringComparison.Ordinal))
                    headerLine = i;
            }

            return headerLine;
        }

        private static string BuildReport(string samplePath, Summary summary)
        {
            StringBuilder builder = new StringBuilder(2048);
            builder.AppendLine("# Visual Performance Last Run");
            builder.AppendLine();
            builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Source CSV: `{samplePath}`");
            builder.AppendLine($"Screenshot directory: `{Path.Combine(Application.persistentDataPath, ScreenshotDirectoryName)}`");
            builder.AppendLine($"CSV session starts at data line: {summary.SessionStartLine}");
            builder.AppendLine();

            if (summary.SampleRows == 0)
            {
                builder.AppendLine("No valid sample rows were found.");
                return builder.ToString();
            }

            float averageMs = summary.AverageSumMs / summary.SampleRows;
            float memoryDeltaMb = summary.EndMemoryMb - summary.StartMemoryMb;
            float durationSeconds = Mathf.Max(0f, summary.LastTimestamp - summary.FirstTimestamp);
            builder.AppendLine("## Summary");
            builder.AppendLine();
            builder.AppendLine("| Metric | Value |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine($"| Scene | {summary.LastScene} |");
            builder.AppendLine($"| Sample rows | {summary.SampleRows} |");
            builder.AppendLine($"| Duration | {durationSeconds:F2} s |");
            builder.AppendLine($"| Average frame time | {averageMs:F2} ms |");
            builder.AppendLine($"| Worst p95 frame time | {summary.MaxP95Ms:F2} ms |");
            builder.AppendLine($"| Worst single frame | {summary.MaxWorstMs:F2} ms |");
            builder.AppendLine($"| Peak CPU frame timing | {FormatOptionalTiming(summary.MaxCpuFrameMs)} |");
            builder.AppendLine($"| Peak GPU frame timing | {FormatOptionalTiming(summary.MaxGpuFrameMs)} |");
            builder.AppendLine($"| Peak GC allocated in frame | {FormatOptionalKilobytes(summary.MaxGcAllocatedKb)} |");
            builder.AppendLine($"| Peak main thread counter | {FormatOptionalTiming(summary.MaxMainThreadMs)} |");
            builder.AppendLine($"| Peak render thread counter | {FormatOptionalTiming(summary.MaxRenderThreadMs)} |");
            builder.AppendLine($"| Managed memory delta | {memoryDeltaMb:F2} MB |");
            builder.AppendLine($"| Time-of-day range | {FormatTimeOfDayRange(summary)} |");
            builder.AppendLine($"| Fog enabled samples | {FormatFogRows(summary)} |");
            builder.AppendLine($"| Peak fog density | {FormatOptionalFloat(summary.MaxFogDensity, "F4")} |");
            builder.AppendLine($"| Fog distance range | {FormatFogDistanceRange(summary)} |");
            builder.AppendLine($"| Peak ambient intensity | {FormatOptionalFloat(summary.MaxAmbientIntensity, "F2")} |");
            builder.AppendLine();
            builder.AppendLine("## Budget Verdict");
            builder.AppendLine();
            builder.AppendLine("| Target | Verdict |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine($"| Desktop 60 FPS | {FormatVerdict(averageMs <= DesktopAverageBudgetMs && summary.MaxP95Ms <= DesktopP95BudgetMs)} |");
            builder.AppendLine($"| WebGL 30 FPS | {FormatVerdict(averageMs <= WebAverageBudgetMs && summary.MaxP95Ms <= WebP95BudgetMs)} |");
            builder.AppendLine($"| Evidence duration | {FormatVerdict(durationSeconds >= MinimumEvidenceDurationSeconds)} |");
            builder.AppendLine($"| Day/night route coverage | {FormatCoverageVerdict(HasDayNightCoverage(summary))} |");
            builder.AppendLine($"| Fog route coverage | {FormatCoverageVerdict(HasFogCoverage(summary))} |");
            builder.AppendLine();
            builder.AppendLine("## Notes");
            builder.AppendLine();
            builder.AppendLine("- Pair this report with a Unity Profiler capture for final merge evidence.");
            builder.AppendLine("- Re-run after render pipeline, cloud, fog, particle, UI animation, camera, or chunk-loading changes.");
            builder.AppendLine("- Review the route screenshots for day, sunset, night fog, and dawn readability before claiming visual polish complete.");
            builder.AppendLine("- Evidence coverage checks prove only that the route sampled required day/night/fog states; screenshots and profiler captures are still required to judge readability and cost.");
            return builder.ToString();
        }

        private static string BuildValidationReport()
        {
            StringBuilder builder = new StringBuilder(4096);
            int failures = 0;
            int warnings = 0;

            builder.AppendLine("# Visual Pipeline Validation");
            builder.AppendLine();
            builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();
            builder.AppendLine("## Automated Checks");
            builder.AppendLine();
            builder.AppendLine("| Check | Verdict | Notes |");
            builder.AppendLine("| --- | --- | --- |");

            string urpAsset = ReadOptional(UrpAssetPath);
            string rendererAsset = ReadOptional(RendererAssetPath);
            string mainScene = ReadOptional(MainScenePath);
            string volumeProfile = ReadOptional(VolumeProfilePath);
            string projectSettings = ReadOptional(ProjectSettingsPath);
            string graphicsSettings = ReadOptional(GraphicsSettingsPath);
            string bootstrapper = ReadOptional(BootstrapperPath);
            string sampler = ReadOptional(SamplerPath);
            string evidenceRouteDriver = ReadOptional(EvidenceRouteDriverPath);
            string reportGenerator = ReadOptional(ReportGeneratorPath);

            AppendCheck(builder, "URP asset exists", urpAsset != null, UrpAssetPath, ref failures);
            AppendCheck(builder, "Renderer asset exists", rendererAsset != null, RendererAssetPath, ref failures);
            AppendCheck(builder, "Main scene uses vibrant profile", Contains(mainScene, VibrantVolumeGuid), MainScenePath, ref failures);
            AppendCheck(builder, "Vibrant profile has tone/color/bloom/vignette", ContainsAll(volumeProfile, "m_Name: Tonemapping", "m_Name: ColorAdjustments", "m_Name: Bloom", "m_Name: Vignette"), VolumeProfilePath, ref failures);
            AppendCheck(builder, "Cloud resolution scale is serialized safely", Contains(rendererAsset, "resolutionScale: 1") || Contains(rendererAsset, "resolutionScale: 2") || Contains(rendererAsset, "resolutionScale: 4"), "Expected Full, Half, or Quarter.", ref failures);
            AppendCheck(builder, "Cloud steps stay inside polish budget", Contains(rendererAsset, "maxSteps: 8") && Contains(rendererAsset, "farStepCount: 3"), "Expected current gameplay cloud budget: 8 near steps, 3 far steps.", ref warnings, true);
            AppendCheck(builder, "SRP batcher enabled", Contains(urpAsset, "m_UseSRPBatcher: 1"), UrpAssetPath, ref warnings, true);
            AppendCheck(builder, "Frame timing stats enabled", Contains(projectSettings, "enableFrameTimingStats: 1"), ProjectSettingsPath, ref failures);
            AppendCheck(builder, "Gameplay post-processing is camera-gated", ContainsAll(bootstrapper, "renderPostProcessing = true", "targetTexture != null", "CameraRenderType.Base"), BootstrapperPath, ref failures);
            AppendCheck(builder, "Sampler writes frame-time CSV", ContainsAll(sampler, "visual-performance-samples.csv", "p95", "FrameTimingManager"), SamplerPath, ref failures);
            AppendCheck(builder, "Sampler delimits each profiling session", ContainsAll(sampler, "EnsureSessionHeader", "File.AppendAllText(_samplePath, CsvHeader"), SamplerPath, ref failures);
            AppendCheck(builder, "Sampler records profiling counters", ContainsAll(sampler, "ProfilerRecorder", "GC Allocated In Frame", "Main Thread", "Render Thread"), SamplerPath, ref warnings, true);
            AppendCheck(builder, "Sampler records visual route state", ContainsAll(sampler, "time_of_day", "RenderSettings.fog", "ambientIntensity"), SamplerPath, ref failures);
            AppendCheck(builder, "Evidence route driver is available", ContainsAll(evidenceRouteDriver, "VisualEvidenceRouteDriver", "day clear", "night fog"), EvidenceRouteDriverPath, ref failures);
            AppendCheck(builder, "Evidence route requests phase samples", ContainsAll(evidenceRouteDriver, "TryWriteImmediateSample", "evidence sample for"), EvidenceRouteDriverPath, ref failures);
            AppendCheck(builder, "Evidence route captures phase screenshots", ContainsAll(evidenceRouteDriver, "ScreenCapture.CaptureScreenshot", "visual-evidence-route", "visual-evidence-"), EvidenceRouteDriverPath, ref failures);
            AppendCheck(builder, "Evidence route can launch from Edit Mode", ContainsAll(reportGenerator, "SessionState", "EnteredPlayMode", "Visual evidence route queued"), ReportGeneratorPath, ref failures);
            AppendCheck(builder, "Evidence route auto-generates report", ContainsAll(reportGenerator, "RunningEvidenceRouteKey", "GenerateReport", "visual performance report generation was requested"), ReportGeneratorPath, ref failures);
            AppendCheck(builder, "Report gates latest-session visual evidence", ContainsAll(sampler, "time_of_day") && ContainsAll(reportGenerator, "FindLatestHeaderLine", "Day/night route coverage", "Fog route coverage", "MinimumEvidenceDurationSeconds"), ReportGeneratorPath, ref failures);
            AppendCheck(builder, "Color space is Linear", Contains(projectSettings, "m_ActiveColorSpace: 1"), ProjectSettingsPath, ref failures);
            AppendCheck(builder, "Lights use linear intensity", Contains(graphicsSettings, "m_LightsUseLinearIntensity: 1"), GraphicsSettingsPath, ref failures);

            builder.AppendLine();
            builder.AppendLine("## Verdict");
            builder.AppendLine();
            if (failures == 0)
            {
                builder.AppendLine(warnings == 0
                    ? "PASS: Visual pipeline setup checks passed. Runtime profiler evidence is still required before closing visual-performance work."
                    : $"PASS WITH WARNINGS: {warnings} warning(s). Runtime profiler evidence is still required before closing visual-performance work.");
            }
            else
            {
                builder.AppendLine($"FAIL: {failures} required check(s) failed and {warnings} warning(s) were found.");
            }

            builder.AppendLine();
            builder.AppendLine("## Required Manual Evidence");
            builder.AppendLine();
            builder.AppendLine("- Run the checklist routes in `Assets/Docs/Visual_Performance_Checklist.md`.");
            builder.AppendLine("- Generate `Assets/Docs/Visual_Performance_Last_Run.md` from a fresh CSV sample.");
            builder.AppendLine("- Attach Unity Profiler CPU Timeline, Rendering, Memory, and screenshots for day, night, fog, and chunk traversal.");
            return builder.ToString();
        }

        private static string ReadOptional(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static bool Contains(string content, string expected)
        {
            return content != null && content.Contains(expected);
        }

        private static bool ContainsAll(string content, params string[] expectedValues)
        {
            if (content == null)
                return false;

            for (int i = 0; i < expectedValues.Length; i++)
            {
                if (!content.Contains(expectedValues[i]))
                    return false;
            }

            return true;
        }

        private static void AppendCheck(StringBuilder builder, string name, bool passed, string notes, ref int count, bool warning = false)
        {
            string verdict = passed ? "PASS" : warning ? "WARN" : "FAIL";
            if (!passed)
                count++;

            builder.AppendLine($"| {name} | {verdict} | {notes} |");
        }

        private static bool TryParse(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseOptional(string[] columns, int index, out float result)
        {
            result = -1f;
            return columns.Length > index && TryParse(columns[index], out result) && result >= 0f;
        }

        private static string FormatOptionalTiming(float value)
        {
            return value < 0f ? "Unavailable" : $"{value:F2} ms";
        }

        private static string FormatOptionalKilobytes(float value)
        {
            return value < 0f ? "Unavailable" : $"{value:F2} KB";
        }

        private static string FormatOptionalFloat(float value, string format)
        {
            return value < 0f || float.IsPositiveInfinity(value) ? "Unavailable" : value.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string FormatTimeOfDayRange(Summary summary)
        {
            return summary.VisualStateRows == 0
                ? "Unavailable"
                : $"{summary.MinTimeOfDay:F3} to {summary.MaxTimeOfDay:F3}";
        }

        private static string FormatFogRows(Summary summary)
        {
            return summary.VisualStateRows == 0
                ? "Unavailable"
                : $"{summary.FogEnabledRows} / {summary.VisualStateRows}";
        }

        private static string FormatFogDistanceRange(Summary summary)
        {
            if (summary.VisualStateRows == 0 || float.IsPositiveInfinity(summary.MinFogStartDistance) || summary.MaxFogEndDistance < 0f)
                return "Unavailable";

            return $"{summary.MinFogStartDistance:F2} to {summary.MaxFogEndDistance:F2}";
        }

        private static string FormatVerdict(bool pass)
        {
            return pass ? "PASS" : "NEEDS PROFILING REVIEW";
        }

        private static string FormatCoverageVerdict(bool pass)
        {
            return pass ? "PASS" : "NEEDS ROUTE COVERAGE";
        }

        private static bool HasDayNightCoverage(Summary summary)
        {
            return summary.VisualStateRows > 0 && summary.MinTimeOfDay <= 0.08f && summary.MaxTimeOfDay >= 0.45f;
        }

        private static bool HasFogCoverage(Summary summary)
        {
            return summary.VisualStateRows > 0 && summary.FogEnabledRows > 0 && summary.MaxFogDensity >= 0.004f;
        }

        private struct Summary
        {
            public int SampleRows;
            public int SessionStartLine;
            public string LastScene;
            public float FirstTimestamp;
            public float LastTimestamp;
            public float AverageSumMs;
            public float MaxP95Ms;
            public float MaxWorstMs;
            public float MaxCpuFrameMs;
            public float MaxGpuFrameMs;
            public float MaxGcAllocatedKb;
            public float MaxMainThreadMs;
            public float MaxRenderThreadMs;
            public int VisualStateRows;
            public int FogEnabledRows;
            public float MinTimeOfDay;
            public float MaxTimeOfDay;
            public float MaxFogDensity;
            public float MinFogStartDistance;
            public float MaxFogEndDistance;
            public float MaxAmbientIntensity;
            public float StartMemoryMb;
            public float EndMemoryMb;
        }
    }
}
