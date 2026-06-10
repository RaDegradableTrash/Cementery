using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 最简单的背景音乐播放器
/// 支持多首音乐顺序或随机循环播放
/// </summary>
public class BackgroundMusicPlayer : MonoBehaviour
{
    [Header("音乐列表")]
    [Tooltip("要播放的背景音乐列表")]
    public List<AudioClip> musicList = new List<AudioClip>();
    
    [Header("播放设置")]
    [Tooltip("是否随机播放（不勾选则按顺序播放）")]
    public bool shuffleMode = false;
    
    [Tooltip("是否循环播放整个列表")]
    public bool loopPlaylist = true;
    
    [Tooltip("音乐淡入淡出时间（秒）")]
    [Range(0f, 2f)]
    public float fadeTime = 1f;
    
    [Header("音量")]
    [Range(0f, 1f)]
    public float volume = 0.5f;
    
    // ── 私有变量 ────────────────────────────────────────────────────────────
    private AudioSource _audioSource;
    private List<int> _playOrder;      // 播放顺序索引列表
    private int _currentIndex = -1;
    private bool _isFading = false;
    
    void Awake()
    {
        SetupAudioSource();
        BuildPlayOrder();
    }
    
    void Start()
    {
        if (musicList.Count > 0)
        {
            PlayNext();
        }
        else
        {
            Debug.LogWarning("背景音乐播放器：音乐列表为空！");
        }
    }
    
    private void SetupAudioSource()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        _audioSource.loop = false;      // 手动控制循环
        _audioSource.playOnAwake = false;
        _audioSource.volume = volume;
        _audioSource.spatialBlend = 0f; // 2D 音乐
    }
    
    /// <summary>
    /// 构建播放顺序（顺序或随机）
    /// </summary>
    private void BuildPlayOrder()
    {
        _playOrder = new List<int>();
        for (int i = 0; i < musicList.Count; i++)
        {
            _playOrder.Add(i);
        }
        
        if (shuffleMode)
        {
            ShuffleList(_playOrder);
        }
    }
    
    /// <summary>
    /// Fisher-Yates 洗牌算法
    /// </summary>
    private void ShuffleList(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);
            (list[i], list[randomIndex]) = (list[randomIndex], list[i]);
        }
    }
    
    /// <summary>
    /// 播放下一首
    /// </summary>
    private void PlayNext()
    {
        if (musicList.Count == 0) return;
        
        // 计算下一个索引
        int nextPlayIndex = _currentIndex + 1;
        
        // 检查是否到达列表末尾
        if (nextPlayIndex >= _playOrder.Count)
        {
            if (loopPlaylist)
            {
                // 循环播放：重新构建顺序（如果随机模式则重新洗牌）
                if (shuffleMode)
                {
                    BuildPlayOrder();
                }
                nextPlayIndex = 0;
            }
            else
            {
                // 不循环：停止播放
                return;
            }
        }
        
        _currentIndex = nextPlayIndex;
        int musicId = _playOrder[_currentIndex];
        AudioClip nextClip = musicList[musicId];
        
        if (nextClip != null)
        {
            StartCoroutine(PlayWithFade(nextClip));
            Debug.Log($"播放背景音乐: {nextClip.name} ({_currentIndex + 1}/{_playOrder.Count})");
        }
        else
        {
            Debug.LogWarning($"音乐列表第 {musicId} 项为空，跳过");
            PlayNext(); // 递归跳过空音乐
        }
    }
    
    private System.Collections.IEnumerator PlayWithFade(AudioClip clip)
    {
        // 如果有音乐正在播放且有淡入淡出时间，先淡出
        if (_audioSource.isPlaying && fadeTime > 0)
        {
            _isFading = true;
            float startVolume = _audioSource.volume;
            float elapsed = 0f;
            
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                _audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeTime);
                yield return null;
            }
            
            _audioSource.Stop();
            _isFading = false;
        }
        else if (_audioSource.isPlaying)
        {
            _audioSource.Stop();
        }
        
        // 播放新音乐
        _audioSource.clip = clip;
        _audioSource.volume = fadeTime > 0 ? 0f : volume;
        _audioSource.Play();
        
        // 淡入
        if (fadeTime > 0)
        {
            _isFading = true;
            float elapsed = 0f;
            
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                _audioSource.volume = Mathf.Lerp(0f, volume, elapsed / fadeTime);
                yield return null;
            }
            
            _audioSource.volume = volume;
            _isFading = false;
        }
        
        // 等待音乐播放完毕
        yield return new WaitForSeconds(clip.length);
        
        // 播放下一首
        PlayNext();
    }
    
    // ── 公共控制方法 ────────────────────────────────────────────────────────
    
    /// <summary>暂停背景音乐</summary>
    public void Pause()
    {
        if (_audioSource.isPlaying)
            _audioSource.Pause();
    }
    
    /// <summary>恢复播放</summary>
    public void Resume()
    {
        if (!_audioSource.isPlaying && _audioSource.clip != null)
            _audioSource.UnPause();
    }
    
    /// <summary>停止播放并重置</summary>
    public void Stop()
    {
        StopAllCoroutines();
        _audioSource.Stop();
        _currentIndex = -1;
        _isFading = false;
    }
    
    /// <summary>重新开始播放（从第一首开始）</summary>
    public void Restart()
    {
        Stop();
        BuildPlayOrder();
        PlayNext();
    }
    
    /// <summary>跳转到下一首</summary>
    public void SkipToNext()
    {
        if (_audioSource.isPlaying)
        {
            StopAllCoroutines();
            _audioSource.Stop();
            PlayNext();
        }
    }
    
    /// <summary>设置音量</summary>
    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        if (!_isFading && _audioSource != null)
            _audioSource.volume = volume;
    }
    
    /// <summary>动态添加音乐（运行时）</summary>
    public void AddMusic(AudioClip clip, bool playImmediately = false)
    {
        if (clip == null) return;
        
        musicList.Add(clip);
        BuildPlayOrder();
        
        if (playImmediately && _audioSource.clip == null)
        {
            PlayNext();
        }
    }
    
    void OnDestroy()
    {
        Stop();
    }
}
