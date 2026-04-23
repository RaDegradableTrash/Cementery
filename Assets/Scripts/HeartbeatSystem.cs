using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class HeartbeatState
{
    public int bpmCurrent = 72;
    public int bpmDisplay = 72;
    public float phase = 0f;
}

public class HeartbeatSystem : MonoBehaviour
{
    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string preferredAnimatorObjectName = "NocturneHeart";
    [SerializeField] private bool autoFindOrCreateAnimator = true;
    [SerializeField] private string heartbeatStateName = "HB_Normal";
    [SerializeField] private string fallbackHeartbeatStateName = "Armature|ArmatureAction";
    [SerializeField] private bool playStateOnEnable = true;
    [SerializeField] private float baseAnimationBPM = 60f;

#if UNITY_EDITOR
    [Header("Editor Auto Wire")]
    [SerializeField] private bool autoWireControllerInEditor = true;
    [SerializeField] private string controllerAssetPath = "Assets/Main Animator Controller.controller";
#endif

    [Header("Heart Rate")]
    [SerializeField] private HeartbeatState state = new HeartbeatState();
    [SerializeField] private float targetBPM = 72f;
    [SerializeField] private float lerpSpeed = 6f;
    [SerializeField] private float minBPM = 20f;
    [SerializeField] private float maxBPM = 220f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lubClip;
    [SerializeField] private AudioClip dubClip;
    [SerializeField, Range(0f, 8f)] private float volumeMultiplier = 3f;
    [SerializeField, Range(0f, 1f)] private float lubPhase = 0.05f;
    [SerializeField, Range(0f, 1f)] private float dubPhase = 0.45f;

    [Header("Debug")]
    [SerializeField] private bool animatorLinked;
    [SerializeField] private bool currentClipIsLooping;
    [SerializeField] private float stuckPhaseTime;

    private float previousPhase;
    private bool phaseInitialized;
    private bool playedLub;
    private bool playedDub;

    private void Awake()
    {
        ResolveAnimator();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
        }

        state.bpmCurrent = Mathf.RoundToInt(Mathf.Clamp(targetBPM, minBPM, maxBPM));
        state.bpmDisplay = state.bpmCurrent;
    }

    private void OnEnable()
    {
        ResolveAnimator();

        if (playStateOnEnable)
        {
            PlayHeartbeatState(0f);
        }

        phaseInitialized = false;
        playedLub = false;
        playedDub = false;
        stuckPhaseTime = 0f;
    }

    private void Update()
    {
        ResolveAnimator();
        if (animator == null)
        {
            return;
        }

        float clampedTarget = Mathf.Clamp(targetBPM, minBPM, maxBPM);
        float bpm = Mathf.Lerp(state.bpmCurrent, clampedTarget, Time.deltaTime * Mathf.Max(0.01f, lerpSpeed));
        state.bpmCurrent = Mathf.RoundToInt(bpm);
        state.bpmDisplay = state.bpmCurrent;

        animator.speed = Mathf.Max(0.01f, state.bpmCurrent / Mathf.Max(1f, baseAnimationBPM));

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        currentClipIsLooping = IsCurrentClipLooping();

        // If clip itself is not looped, force replay near the end.
        if (!currentClipIsLooping && info.normalizedTime >= 0.98f)
        {
            PlayHeartbeatState(0f);
            phaseInitialized = false;
            return;
        }

        float phase = Mathf.Repeat(info.normalizedTime, 1f);
        state.phase = phase;

        if (!phaseInitialized)
        {
            previousPhase = phase;
            phaseInitialized = true;
            return;
        }

        float delta = Mathf.Abs(phase - previousPhase);
        if (delta < 0.0005f)
        {
            stuckPhaseTime += Time.deltaTime;
            if (stuckPhaseTime > 0.5f)
            {
                PlayHeartbeatState(0f);
                phaseInitialized = false;
                return;
            }
        }
        else
        {
            stuckPhaseTime = 0f;
        }

        if (phase < previousPhase)
        {
            playedLub = false;
            playedDub = false;
        }

        if (!playedLub && Crossed(previousPhase, phase, lubPhase))
        {
            PlaySfx(lubClip, 1f);
            playedLub = true;
        }

        if (!playedDub && Crossed(previousPhase, phase, dubPhase))
        {
            PlaySfx(dubClip, 0.85f);
            playedDub = true;
        }

        previousPhase = phase;
    }

    private void ResolveAnimator()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        GameObject preferred = null;
        if (animator == null && !string.IsNullOrEmpty(preferredAnimatorObjectName))
        {
            preferred = GameObject.Find(preferredAnimatorObjectName);
            if (preferred != null)
            {
                animator = preferred.GetComponent<Animator>();
            }
        }

        if (animator == null && autoFindOrCreateAnimator && preferred != null)
        {
            animator = preferred.AddComponent<Animator>();
        }

#if UNITY_EDITOR
        if (animator != null && autoWireControllerInEditor && animator.runtimeAnimatorController == null)
        {
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerAssetPath);
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
                EditorUtility.SetDirty(animator);
            }
        }
#endif

        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        animatorLinked = animator != null;
    }

    private void PlayHeartbeatState(float normalizedTime)
    {
        if (animator == null)
        {
            return;
        }

        string stateName = ResolveStateName();
        if (string.IsNullOrEmpty(stateName))
        {
            return;
        }

        int fullHash = Animator.StringToHash("Base Layer." + stateName);
        if (animator.HasState(0, fullHash))
        {
            animator.Play(fullHash, 0, Mathf.Clamp01(normalizedTime));
            return;
        }

        animator.Play(stateName, 0, Mathf.Clamp01(normalizedTime));
    }

    private string ResolveStateName()
    {
        if (HasState(heartbeatStateName))
        {
            return heartbeatStateName;
        }

        if (HasState(fallbackHeartbeatStateName))
        {
            return fallbackHeartbeatStateName;
        }

        return null;
    }

    private bool HasState(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
        {
            return false;
        }

        int shortHash = Animator.StringToHash(stateName);
        int fullHash = Animator.StringToHash("Base Layer." + stateName);
        return animator.HasState(0, shortHash) || animator.HasState(0, fullHash);
    }

    private bool IsCurrentClipLooping()
    {
        if (animator == null)
        {
            return false;
        }

        AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
        if (clips.Length == 0 || clips[0].clip == null)
        {
            return false;
        }

        return clips[0].clip.isLooping;
    }

    private void PlaySfx(AudioClip clip, float scale)
    {
        if (audioSource == null || clip == null)
        {
            return;
        }

        audioSource.PlayOneShot(clip, volumeMultiplier * scale);
    }

    private static bool Crossed(float from, float to, float point)
    {
        if (to >= from)
        {
            return point >= from && point < to;
        }

        return point >= from || point < to;
    }

    public void SetTargetBPM(float bpm)
    {
        targetBPM = Mathf.Clamp(bpm, minBPM, maxBPM);
    }
}
