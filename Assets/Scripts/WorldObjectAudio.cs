using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 附加到带有 WorldObject 组件的对象上，处理各种交互音效。
/// 通过 Inspector 直接拖拽 MP3/音频文件即可使用。
/// </summary>
[RequireComponent(typeof(WorldObject))]
public class WorldObjectAudio : MonoBehaviour
{
    [Header("音效设置")]
    [Tooltip("音效输出总开关，不勾选则所有音效静音")]
    public bool enableSounds = true;
    
    [Tooltip("使用的 AudioMixerGroup（可选，留空则使用默认输出）")]
    public AudioMixerGroup audioMixerGroup;
    
    [Header("交互音效")]
    [Tooltip("按下交互键（默认 F）时的音效")]
    public AudioClip interactSound;
    
    [Tooltip("拾取时的音效")]
    public AudioClip pickUpSound;
    
    [Tooltip("放下时的音效")]
    public AudioClip dropSound;
    
    [Tooltip("收集/拾取消失时的音效")]
    public AudioClip collectSound;
    
    [Header("音量控制")]
    [Range(0f, 1f)]
    public float masterVolume = 1f;
    
    [Range(0f, 1f)]
    public float interactVolume = 1f;
    
    [Range(0f, 1f)]
    public float pickUpVolume = 1f;
    
    [Range(0f, 1f)]
    public float dropVolume = 1f;
    
    [Range(0f, 1f)]
    public float collectVolume = 1f;
    
    [Header("音效变化（可选）")]
    [Tooltip("拾取时随机选择多个音效中的一个，不为空时会覆盖 pickUpSound")]
    public AudioClip[] randomPickUpSounds;
    
    [Tooltip("放下时随机选择多个音效中的一个，不为空时会覆盖 dropSound")]
    public AudioClip[] randomDropSounds;
    
    [Header("2D/3D 音效设置")]
    [Tooltip("音效是否为 2D（不随距离衰减）")]
    public bool is2DSound = false;
    
    [Tooltip("3D 音效的最大距离")]
    public float maxDistance = 20f;
    
    [Tooltip("3D 音效的最小距离")]
    public float minDistance = 1f;
    
    private WorldObject _worldObject;
    private AudioSource _audioSource;
    
    void Awake()
    {
        _worldObject = GetComponent<WorldObject>();
        SetupAudioSource();
    }
    
    void OnEnable()
    {
        // 绑定 WorldObject 的事件
        if (_worldObject != null)
        {
            _worldObject.onInteract.AddListener(OnInteractHandler);
            _worldObject.onPickUp.AddListener(OnPickUpHandler);
            _worldObject.onDrop.AddListener(OnDropHandler);
            _worldObject.onCollect.AddListener(OnCollectHandler);
        }
    }
    
    void OnDisable()
    {
        // 解绑事件，防止内存泄漏
        if (_worldObject != null)
        {
            _worldObject.onInteract.RemoveListener(OnInteractHandler);
            _worldObject.onPickUp.RemoveListener(OnPickUpHandler);
            _worldObject.onDrop.RemoveListener(OnDropHandler);
            _worldObject.onCollect.RemoveListener(OnCollectHandler);
        }
    }
    
    private void SetupAudioSource()
    {
        // 获取或添加 AudioSource
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // 配置 AudioSource
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = is2DSound ? 0f : 1f;
        _audioSource.maxDistance = maxDistance;
        _audioSource.minDistance = minDistance;
        
        if (audioMixerGroup != null)
        {
            _audioSource.outputAudioMixerGroup = audioMixerGroup;
        }
    }
    
    private void PlaySound(AudioClip clip, float volumeMultiplier = 1f)
    {
        if (!enableSounds || clip == null || _audioSource == null)
            return;
        
        float finalVolume = masterVolume * volumeMultiplier;
        _audioSource.PlayOneShot(clip, finalVolume);
    }
    
    private AudioClip GetRandomClip(AudioClip[] clips, AudioClip defaultClip)
    {
        if (clips != null && clips.Length > 0)
        {
            return clips[Random.Range(0, clips.Length)];
        }
        return defaultClip;
    }
    
    private void OnInteractHandler(GameObject actor)
    {
        PlaySound(interactSound, interactVolume);
    }
    
    private void OnPickUpHandler(GameObject actor)
    {
        AudioClip clip = GetRandomClip(randomPickUpSounds, pickUpSound);
        PlaySound(clip, pickUpVolume);
    }
    
    private void OnDropHandler(GameObject actor)
    {
        AudioClip clip = GetRandomClip(randomDropSounds, dropSound);
        PlaySound(clip, dropVolume);
    }
    
    private void OnCollectHandler(GameObject actor)
    {
        PlaySound(collectSound, collectVolume);
    }
    
    // ── 公共方法，供外部手动调用 ──────────────────────────────────────────────
    
    /// <summary>手动播放自定义音效</summary>
    public void PlayCustomSound(AudioClip clip, float volumeMultiplier = 1f)
    {
        PlaySound(clip, volumeMultiplier);
    }
    
    /// <summary>播放一组随机音效</summary>
    public void PlayRandomSound(AudioClip[] clips, float volumeMultiplier = 1f)
    {
        if (clips != null && clips.Length > 0)
        {
            PlaySound(clips[Random.Range(0, clips.Length)], volumeMultiplier);
        }
    }
    
    /// <summary>立即停止当前正在播放的音效</summary>
    public void StopCurrentSound()
    {
        if (_audioSource != null && _audioSource.isPlaying)
        {
            _audioSource.Stop();
        }
    }
}