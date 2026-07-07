using UnityEngine;

namespace Cementery.Rendering
{
    public sealed class VisualEvidenceRouteDriver : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const float DefaultPhaseDurationSeconds = 10f;

        private static readonly Phase[] Phases =
        {
            new Phase("day clear", 0.32f, false, 0.0005f, 35f, 260f, new Color(0.58f, 0.66f, 0.74f, 1f), 1.2f),
            new Phase("sunset haze", 0.48f, true, 0.0045f, 18f, 170f, new Color(0.9f, 0.52f, 0.38f, 1f), 1.05f),
            new Phase("night fog", 0.02f, true, 0.014f, 8f, 95f, new Color(0.03f, 0.045f, 0.075f, 1f), 0.75f),
            new Phase("dawn fog", 0.24f, true, 0.006f, 14f, 145f, new Color(0.62f, 0.56f, 0.66f, 1f), 0.95f)
        };

        [SerializeField, Min(3f)] private float phaseDurationSeconds = DefaultPhaseDurationSeconds;
        [SerializeField] private bool restoreOriginalState = true;

        private DayNightSkyboxController _dayNightController;
        private FogState _originalFog;
        private bool _originalAutoAdvance;
        private bool _originalControlFog;
        private float _originalTimeOfDay;
        private float _originalAmbientIntensity;
        private float _phaseTimer;
        private int _phaseIndex = -1;
        private bool _restored;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _dayNightController = FindFirstObjectByType<DayNightSkyboxController>(FindObjectsInactive.Include);
            if (_dayNightController == null)
            {
                Debug.LogWarning("VisualEvidenceRouteDriver: no DayNightSkyboxController was found, so the evidence route cannot run.");
                Destroy(gameObject);
                return;
            }

            _originalAutoAdvance = _dayNightController.autoAdvance;
            _originalControlFog = _dayNightController.controlFog;
            _originalTimeOfDay = _dayNightController.timeOfDay;
            _originalAmbientIntensity = RenderSettings.ambientIntensity;
            _originalFog = FogState.Capture();
            _dayNightController.autoAdvance = false;
            _dayNightController.controlFog = false;

            AdvancePhase();
            Debug.Log($"VisualEvidenceRouteDriver: started {Phases.Length} visual evidence phases at {phaseDurationSeconds:F1}s each.");
        }

        private void Update()
        {
            if (_dayNightController == null)
            {
                FinishRoute();
                return;
            }

            _phaseTimer += Time.unscaledDeltaTime;
            if (_phaseTimer < phaseDurationSeconds)
                return;

            AdvancePhase();
        }

        private void OnDestroy()
        {
            if (restoreOriginalState)
                RestoreOriginalState();
        }

        private void AdvancePhase()
        {
            _phaseIndex++;
            _phaseTimer = 0f;

            if (_phaseIndex >= Phases.Length)
            {
                FinishRoute();
                return;
            }

            Phase phase = Phases[_phaseIndex];
            _dayNightController.timeOfDay = phase.TimeOfDay;
            RenderSettings.fog = phase.FogEnabled;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = phase.FogDensity;
            RenderSettings.fogStartDistance = phase.FogStartDistance;
            RenderSettings.fogEndDistance = phase.FogEndDistance;
            RenderSettings.fogColor = phase.FogColor;
            RenderSettings.ambientIntensity = phase.AmbientIntensity;

            Debug.Log($"VisualEvidenceRouteDriver: phase {_phaseIndex + 1}/{Phases.Length} {phase.Name}, time {phase.TimeOfDay:F2}, fog {phase.FogEnabled}.");
        }

        private void FinishRoute()
        {
            if (restoreOriginalState)
                RestoreOriginalState();

            Debug.Log("VisualEvidenceRouteDriver: route complete. Generate the visual performance report after samples are written.");
            Destroy(gameObject);
        }

        private void RestoreOriginalState()
        {
            if (_restored)
                return;

            _restored = true;
            if (_dayNightController != null)
            {
                _dayNightController.autoAdvance = _originalAutoAdvance;
                _dayNightController.controlFog = _originalControlFog;
                _dayNightController.timeOfDay = _originalTimeOfDay;
            }

            _originalFog.Apply();
            RenderSettings.ambientIntensity = _originalAmbientIntensity;
        }

        private readonly struct Phase
        {
            public readonly string Name;
            public readonly float TimeOfDay;
            public readonly bool FogEnabled;
            public readonly float FogDensity;
            public readonly float FogStartDistance;
            public readonly float FogEndDistance;
            public readonly Color FogColor;
            public readonly float AmbientIntensity;

            public Phase(string name, float timeOfDay, bool fogEnabled, float fogDensity, float fogStartDistance, float fogEndDistance, Color fogColor, float ambientIntensity)
            {
                Name = name;
                TimeOfDay = timeOfDay;
                FogEnabled = fogEnabled;
                FogDensity = fogDensity;
                FogStartDistance = fogStartDistance;
                FogEndDistance = fogEndDistance;
                FogColor = fogColor;
                AmbientIntensity = ambientIntensity;
            }
        }

        private readonly struct FogState
        {
            private readonly bool _enabled;
            private readonly FogMode _mode;
            private readonly float _density;
            private readonly float _startDistance;
            private readonly float _endDistance;
            private readonly Color _color;

            private FogState(bool enabled, FogMode mode, float density, float startDistance, float endDistance, Color color)
            {
                _enabled = enabled;
                _mode = mode;
                _density = density;
                _startDistance = startDistance;
                _endDistance = endDistance;
                _color = color;
            }

            public static FogState Capture()
            {
                return new FogState(RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogDensity, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, RenderSettings.fogColor);
            }

            public void Apply()
            {
                RenderSettings.fog = _enabled;
                RenderSettings.fogMode = _mode;
                RenderSettings.fogDensity = _density;
                RenderSettings.fogStartDistance = _startDistance;
                RenderSettings.fogEndDistance = _endDistance;
                RenderSettings.fogColor = _color;
            }
        }
#endif
    }
}
