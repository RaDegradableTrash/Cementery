using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class PlayerFootstepAudio : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("拖入玩家的 PlayerController 脚本")]
    [SerializeField] private PlayerController playerController;

    [Header("Audio Clip Config")]
    [Tooltip("走路的循环音效文件")]
    [SerializeField] private AudioClip walkSound;
    
    [Tooltip("【掐头】从第几秒开始播放（单位：秒）")]
    [SerializeField] private float trimStart = 0.0f;
    
    [Tooltip("【去尾】在第几秒时立刻折返重新播放（单位：秒）。填 0 则自动播放到文件结束")]
    [SerializeField] private float trimEnd = 0.0f;

    [Header("Audio Settings")]
    [Range(0f, 1f)] [SerializeField] private float volume = 0.6f;
    [Tooltip("运动状态检测的最小速度阈值")]
    [SerializeField] private float speedThreshold = 0.2f;

    private AudioSource _audioSource;
    private Rigidbody _playerRb;
    private InventoryCameraController _inventoryCameraController;
    private float _actualEndTime;
    private float _nextInventoryCheckTime;
    private bool _cachedInventoryActive;
    private AudioClip _lastAssignedClip;
    private float _lastAssignedVolume = -1f;
    private float _cachedSpeedThreshold = -1f;
    private float _cachedSpeedThresholdSqr;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.loop = false; // 关键：关闭自带Loop，由我们的代码精准控制截断循环

        if (playerController != null)
        {
            _playerRb = playerController.GetComponent<Rigidbody>();
        }
        _inventoryCameraController = InventoryCameraController.GetPrimaryController();
    }

    void Start()
    {
        // 计算实际的结束截断点
        UpdateTrimSettings();
    }

    void OnValidate()
    {
        // 方便在 Inspector 实时调整参数时刷新数据
        if (walkSound != null) UpdateTrimSettings();
    }

    private void UpdateTrimSettings()
    {
        if (walkSound == null) return;
        
        // 限制掐头时间不能超过音频总长
        trimStart = Mathf.Clamp(trimStart, 0f, walkSound.length - 0.05f);
        
        // 如果去尾没设置或者设置错误，默认就是音频文件尾部
        if (trimEnd <= trimStart || trimEnd > walkSound.length)
        {
            _actualEndTime = walkSound.length;
        }
        else
        {
            _actualEndTime = trimEnd;
        }
    }

    void Update()
    {
        if (playerController == null || _playerRb == null || walkSound == null) return;

        // 1. 从 PlayerController 的核心状态中判断玩家目前是否能走、想走
        float thresholdSqr = GetSpeedThresholdSqr();
        bool isMoving = _playerRb.velocity.sqrMagnitude > thresholdSqr;
        
        // 联动 PlayerController 里的各种状态拦截
        bool canPlayFootstep = isMoving 
                               && !playerController.IsUsingFurniture   // 没在使用家具
                               && playerController.hp > 0               // 活着
                               && !IsInventoryModeActive();            // 没打开背包

        // 2. 状态机控制播放与重置
        if (canPlayFootstep)
        {
            if (!_audioSource.isPlaying)
            {
                // 首次触发或被停掉后重新起播
                if (_lastAssignedClip != walkSound)
                {
                    _audioSource.clip = walkSound;
                    _lastAssignedClip = walkSound;
                }
                if (!Mathf.Approximately(_lastAssignedVolume, volume))
                {
                    _audioSource.volume = volume;
                    _lastAssignedVolume = volume;
                }
                _audioSource.time = trimStart; // 卡准【掐头】点
                _audioSource.Play();
            }
            else
            {
                // 3. 关键核心：实时检测是否达到了【去尾】的截断点
                if (_audioSource.time >= _actualEndTime)
                {
                    // 触及尾部截断点，瞬间拉回【掐头】点，实现完美无缝切片循环
                    _audioSource.time = trimStart;
                }
            }
        }
        else
        {
            // 玩家停下、死掉或坐下时，立刻淡出或停止声音
            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
        }
    }

    // 辅助反射获取 PlayerController 里的私有背包打开状态方法
    private bool IsInventoryModeActive()
    {
        // 这里的逻辑直接参考了你 PlayerController 内部的判定
        if (Time.unscaledTime < _nextInventoryCheckTime)
            return _cachedInventoryActive;

        _nextInventoryCheckTime = Time.unscaledTime + 0.1f;
        if (_inventoryCameraController == null)
            _inventoryCameraController = InventoryCameraController.GetPrimaryController();

        _cachedInventoryActive = _inventoryCameraController != null && _inventoryCameraController.IsInventoryActive;
        return _cachedInventoryActive;
    }

    private float GetSpeedThresholdSqr()
    {
        if (!Mathf.Approximately(_cachedSpeedThreshold, speedThreshold))
        {
            _cachedSpeedThreshold = speedThreshold;
            _cachedSpeedThresholdSqr = speedThreshold * speedThreshold;
        }

        return _cachedSpeedThresholdSqr;
    }
}
