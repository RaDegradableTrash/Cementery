using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class CockpitAudioSystem : MonoBehaviour, IGearAudioPlayer
{
    [Header("Gear Audio Clips")]
    [Tooltip("换挡成功音效")]
    [SerializeField] private AudioClip shiftSuccessClip;
    [Tooltip("换挡失败音效")]
    [SerializeField] private AudioClip shiftFailClip;

    [Header("Audio Settings")]
    [Range(0f, 1f)] [SerializeField] private float volume = 0.8f;

    private AudioSource _audioSource;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        
        // 关键一步：在游戏启动时，把自己注册给 GearButtonBase 的接口接口
        GearButtonBase.AudioPlayer = this;
    }

    void OnDestroy()
    {
        // 游戏结束或销毁时断开引用，防止内存泄漏
        if (GearButtonBase.AudioPlayer == (IGearAudioPlayer)this)
        {
            GearButtonBase.AudioPlayer = null;
        }
    }

    // ── 实现接口方法 ────────────────────────────────────────────────────────
    
    public void PlayShiftSuccess(CarControl.GearMode gear)
    {
        if (shiftSuccessClip != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(shiftSuccessClip, volume);
            
            // 提示：因为把 gear 传进来了，如果你以后想做“倒车档音效不一样”，可以写成：
            // if(gear == CarControl.GearMode.Reverse) { ... }
        }
    }

    public void PlayShiftFail(CarControl.GearMode gear)
    {
        if (shiftFailClip != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(shiftFailClip, volume);
        }
    }
}