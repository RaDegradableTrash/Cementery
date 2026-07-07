using System;
using System.IO;
using Unity.Profiling;
using UnityEngine;

namespace Cementery.Rendering
{
    public sealed class VisualPerformanceSampler : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const int MaxSamples = 4096;
        private const float ReportIntervalSeconds = 5f;
        private const string SampleFileName = "visual-performance-samples.csv";
        private const string CsvHeader = "time,scene,average_ms,p95_ms,worst_ms,cpu_frame_ms,gpu_frame_ms,sample_count,total_memory_mb,memory_delta_mb,gc_allocated_kb,main_thread_ms,render_thread_ms";

        private readonly float[] _frameTimesMs = new float[MaxSamples];
        private readonly float[] _sortBuffer = new float[MaxSamples];
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
        private ProfilerRecorder _gcAllocatedRecorder;
        private ProfilerRecorder _mainThreadRecorder;
        private ProfilerRecorder _renderThreadRecorder;
        private int _sampleCount;
        private int _sampleCursor;
        private float _elapsedSinceReport;
        private float _worstFrameMs;
        private long _lastTotalMemory;
        private string _samplePath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<VisualPerformanceSampler>(FindObjectsInactive.Include) != null)
                return;

            GameObject go = new GameObject("VisualPerformanceSampler");
            DontDestroyOnLoad(go);
            go.AddComponent<VisualPerformanceSampler>();
        }

        private void Awake()
        {
            _samplePath = Path.Combine(Application.persistentDataPath, SampleFileName);
            _lastTotalMemory = GC.GetTotalMemory(false);
            _gcAllocatedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            _renderThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread");

            if (!File.Exists(_samplePath))
            {
                File.WriteAllText(_samplePath, CsvHeader + "\n");
            }
            else if (!File.ReadAllText(_samplePath).Contains("gc_allocated_kb"))
            {
                File.AppendAllText(_samplePath, CsvHeader + "\n");
            }
        }

        private void OnDestroy()
        {
            DisposeRecorder(ref _gcAllocatedRecorder);
            DisposeRecorder(ref _mainThreadRecorder);
            DisposeRecorder(ref _renderThreadRecorder);
        }

        private void Update()
        {
            FrameTimingManager.CaptureFrameTimings();

            float frameMs = Time.unscaledDeltaTime * 1000f;
            _frameTimesMs[_sampleCursor] = frameMs;
            _sampleCursor = (_sampleCursor + 1) % MaxSamples;
            _sampleCount = Mathf.Min(_sampleCount + 1, MaxSamples);
            _worstFrameMs = Mathf.Max(_worstFrameMs, frameMs);

            _elapsedSinceReport += Time.unscaledDeltaTime;
            if (_elapsedSinceReport < ReportIntervalSeconds)
                return;

            WriteReport();
            _elapsedSinceReport = 0f;
            _worstFrameMs = 0f;
        }

        private void WriteReport()
        {
            if (_sampleCount == 0)
                return;

            float total = 0f;
            for (int i = 0; i < _sampleCount; i++)
            {
                float value = _frameTimesMs[i];
                _sortBuffer[i] = value;
                total += value;
            }

            Array.Sort(_sortBuffer, 0, _sampleCount);
            int p95Index = Mathf.Clamp(Mathf.CeilToInt(_sampleCount * 0.95f) - 1, 0, _sampleCount - 1);
            float averageMs = total / _sampleCount;
            float p95Ms = _sortBuffer[p95Index];
            float cpuFrameMs = -1f;
            float gpuFrameMs = -1f;
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0)
            {
                cpuFrameMs = (float)_frameTimings[0].cpuFrameTime;
                gpuFrameMs = (float)_frameTimings[0].gpuFrameTime;
            }

            long totalMemory = GC.GetTotalMemory(false);
            float totalMemoryMb = totalMemory / (1024f * 1024f);
            float memoryDeltaMb = (totalMemory - _lastTotalMemory) / (1024f * 1024f);
            _lastTotalMemory = totalMemory;
            float gcAllocatedKb = ReadCounterKilobytes(_gcAllocatedRecorder);
            float mainThreadMs = ReadCounterMilliseconds(_mainThreadRecorder);
            float renderThreadMs = ReadCounterMilliseconds(_renderThreadRecorder);

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string line = $"{Time.realtimeSinceStartup:F2},{sceneName},{averageMs:F2},{p95Ms:F2},{_worstFrameMs:F2},{cpuFrameMs:F2},{gpuFrameMs:F2},{_sampleCount},{totalMemoryMb:F2},{memoryDeltaMb:F2},{gcAllocatedKb:F2},{mainThreadMs:F2},{renderThreadMs:F2}\n";
            File.AppendAllText(_samplePath, line);
            Debug.Log($"VisualPerformanceSampler: avg {averageMs:F2} ms, p95 {p95Ms:F2} ms, worst {_worstFrameMs:F2} ms, CPU {cpuFrameMs:F2} ms, GPU {gpuFrameMs:F2} ms, GC {gcAllocatedKb:F2} KB, main {mainThreadMs:F2} ms, render {renderThreadMs:F2} ms, samples {_sampleCount}, csv {_samplePath}");
        }

        private static float ReadCounterKilobytes(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue / 1024f : -1f;
        }

        private static float ReadCounterMilliseconds(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue / 1000000f : -1f;
        }

        private static void DisposeRecorder(ref ProfilerRecorder recorder)
        {
            if (recorder.Valid)
                recorder.Dispose();
        }
#endif
    }
}
