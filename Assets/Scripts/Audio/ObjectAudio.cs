using UnityEngine;

[RequireComponent(typeof(WorldObject))]
[RequireComponent(typeof(AudioSource))]
public class ObjectAudio : MonoBehaviour
{
    // 定义一个结构体，把音频和它的配置配置在一起
    [System.Serializable]
    public struct SoundConfig
    {
        [Tooltip("音频剪辑")]
        public AudioClip clip;
        [Tooltip("从第几秒开始播放（单位：秒）。比如 0.1 表示跳过前 100 毫秒的空白")]
        public float startTime;
    }

    [Header("Audio Clips Config")]
    [Tooltip("音效：[F] 触发交互")]
    [SerializeField] private SoundConfig interactSound;
    
    [Tooltip("音效：[LMB] 拿起物品")]
    [SerializeField] private SoundConfig pickUpSound;
    
    [Tooltip("音效：[LMB] 放下/扔掉物品")]
    [SerializeField] private SoundConfig dropSound;
    
    [Tooltip("音效：[RMB] 收集")]
    [SerializeField] private SoundConfig collectSound;

    [Header("Audio Settings")]
    [Range(0f, 1f)] [SerializeField] private float volume = 1.0f;
    [Range(0.5f, 1.5f)] [SerializeField] private float pitchRandomness = 0.05f;

    private WorldObject _worldObject;
    private AudioSource _audioSource;
    private float _basePitch;

    void Awake()
    {
        _worldObject = GetComponent<WorldObject>();
        _audioSource = GetComponent<AudioSource>();
        _basePitch = _audioSource.pitch;

        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 1.0f; // 3D 音效
    }

    void OnEnable()
    {
        if (_worldObject != null)
        {
            _worldObject.onInteract.AddListener(PlayInteractSound);
            _worldObject.onPickUp.AddListener(PlayPickUpSound);
            _worldObject.onDrop.AddListener(PlayDropSound);
            _worldObject.onCollect.AddListener(PlayCollectSound);
        }
    }

    void OnDisable()
    {
        if (_worldObject != null)
        {
            _worldObject.onInteract.RemoveListener(PlayInteractSound);
            _worldObject.onPickUp.RemoveListener(PlayPickUpSound);
            _worldObject.onDrop.RemoveListener(PlayDropSound);
            _worldObject.onCollect.RemoveListener(PlayCollectSound);
        }
    }

    // ── 核心播放逻辑 ─────────────────────────────────────────────────────────
    
    private void PlaySound(SoundConfig config, bool playAtPointIfDestroyed = false)
    {
        if (config.clip == null) return;

        // 1. 计算随机音高
        float randomPitch = _basePitch + Random.Range(-pitchRandomness, pitchRandomness);

        // 2. 如果是收集(Collect)，物体即将销毁，我们需要特殊处理
        if (playAtPointIfDestroyed)
        {
            // 缺点：PlayClipAtPoint 内部无法直接控制 time。
            // 解决方案：动态创建一个临时音频临时载体，播完自毁，完美支持 time 和 pitch！
            GameObject tempAudioObj = new GameObject("TempAudio_Collect");
            tempAudioObj.transform.position = transform.position;
            
            AudioSource tempSource = tempAudioObj.AddComponent<AudioSource>();
            tempSource.clip = config.clip;
            tempSource.volume = volume;
            tempSource.pitch = randomPitch;
            tempSource.spatialBlend = 1.0f;
            
            // 关键：设置起始时间并播放
            tempSource.time = Mathf.Clamp(config.startTime, 0f, config.clip.length - 0.01f);
            tempSource.Play();
            
            // 播放完毕后连同载体一起销毁
            Destroy(tempAudioObj, config.clip.length - config.startTime + 0.1f);
        }
        else
        {
            // 3. 常规交互（物体不销毁）：直接使用自身的 AudioSource
            if (_audioSource == null) return;
            
            _audioSource.clip = config.clip;
            _audioSource.volume = volume;
            _audioSource.pitch = randomPitch;
            
            // 关键：设置起播时间（确保不会超过音频总长）
            _audioSource.time = Mathf.Clamp(config.startTime, 0f, config.clip.length - 0.01f);
            _audioSource.Play();
        }
    }

    // ── 事件回调函数 ─────────────────────────────────────────────────────────
    
    private void PlayInteractSound(GameObject actor) => PlaySound(interactSound);
    private void PlayPickUpSound(GameObject actor)   => PlaySound(pickUpSound);
    private void PlayDropSound(GameObject actor)     => PlaySound(dropSound);
    
    private void PlayCollectSound(GameObject actor)
    {
        PlaySound(collectSound, playAtPointIfDestroyed: true);
    }
}