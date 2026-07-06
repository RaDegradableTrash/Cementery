using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 收音机控制器 - 叠加功能，不干扰 WorldObject 原有交互
/// F键：切换音乐（与原有interact共存）
/// 中键：暂停/播放音乐
/// </summary>
[RequireComponent(typeof(WorldObject))]
[RequireComponent(typeof(AudioSource))]
public class Speaker : MonoBehaviour
{
    [Header("音乐列表")]
    [Tooltip("拖入想要播放的音频文件")]
    public List<AudioClip> musicPlaylist = new List<AudioClip>();
    
    [Tooltip("音乐名称列表（留空则自动使用音频文件名）")]
    public List<string> songNames = new List<string>();
    
    [Header("播放设置")]
    [Tooltip("是否自动开始播放第一首歌")]
    public bool autoPlayOnStart = true;
    
    [Tooltip("播放完列表后是否循环（回到第一首）")]
    public bool loopPlaylist = true;
    
    [Tooltip("播放淡入淡出时间（秒）")]
    [Range(0f, 3f)]
    public float fadeTime = 0.5f;
    
    [Header("音量设置")]
    [Range(0f, 1f)]
    public float masterVolume = 0.7f;
    
    [Tooltip("距离衰减曲线")]
    public AnimationCurve distanceRolloff = AnimationCurve.Linear(0f, 1f, 20f, 0f);
    [Tooltip("最大可听距离")]
    public float maxDistance = 30f;
    
    [Header("显示设置")]
    [Tooltip("用于显示歌曲名称的 TextMeshPro")]
    public TextMeshPro displayTextMeshPro;
    [Tooltip("普通 Text（二选一）")]
    public Text displayText;
    [Tooltip("显示格式：{0}=歌名，{1}=当前序号，{2}=总曲目")]
    public string displayFormat = "🎵 {0}\n{1}/{2}";
    [Tooltip("无音乐时显示")]
    public string noMusicText = "📻 无音乐";
    [Tooltip("暂停后缀")]
    public string pausedSuffix = " ⏸";
    [Tooltip("播放后缀")]
    public string playingSuffix = " ▶";
    
    // ── 私有变量 ────────────────────────────────────────────────────────────
    private AudioSource _audioSource;
    private WorldObject _worldObject;
    private int _currentTrackIndex = -1;
    private bool _isPlaying = false;
    private Coroutine _fadeCoroutine;
    private Coroutine _trackEndCoroutine;
    private string _lastDisplayString;
    
    // 属性
    public int CurrentTrackIndex => _currentTrackIndex;
    public bool IsPlaying => _isPlaying;
    public string CurrentSongName => GetSongName(_currentTrackIndex);
    public int TotalSongs => musicPlaylist.Count;
    
    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _worldObject = GetComponent<WorldObject>();
        if (_worldObject != null)
        {
            _worldObject.onInteract.AddListener(OnInteractWithRadio);
        }

        SetupAudioSource();
    }
    
    void Start()
    {
        UpdateDisplay();
        
        if (autoPlayOnStart && musicPlaylist.Count > 0)
        {
            PlayTrack(0);
        }
        else if (musicPlaylist.Count == 0)
        {
            Debug.LogWarning($"收音机 {gameObject.name} 的音乐列表为空！");
            UpdateDisplay();
        }
    }
    
    void OnDestroy()
    {
        if (_worldObject != null)
        {
            _worldObject.onInteract.RemoveListener(OnInteractWithRadio);
        }
    }
    
    private System.Collections.IEnumerator TrackEndWatcher()
    {
        while (_isPlaying && musicPlaylist.Count > 0 && _audioSource != null)
        {
            yield return new WaitForSecondsRealtime(0.2f);

            // 自动播放下一首（当前歌曲播放完毕时）
            if (_isPlaying && !_audioSource.isPlaying)
            {
                NextTrack();
                yield break;
            }
        }
    }
    
    // ── 中键检测（独立于 WorldObject）────────────────────────────────────────
    // WorldObject 原本没有中键功能，所以直接添加不会有冲突
    void OnMouseOver()
    {
        // 检测中键点击（Button 2）
        if (Input.GetMouseButtonDown(2))
        {
            OnMiddleClick();
        }
    }
    
    // ── 交互回调 ────────────────────────────────────────────────────────────
    
    /// <summary>
    /// F键交互时调用 - 切换下一首音乐
    /// 与原有的 WorldObject.onInteract 事件共存
    /// </summary>
    private void OnInteractWithRadio(GameObject actor)
    {
        if (musicPlaylist.Count == 0) return;
        
        // 切换到下一首
        NextTrack();
        
        // 可选：显示提示（通过 WorldObject 的 interactMessage）
        if (_worldObject != null && !string.IsNullOrEmpty(CurrentSongName))
        {
            // 临时显示当前歌曲名（不影响原有消息）
            Debug.Log($"[收音机] 切换到: {CurrentSongName}");
        }
    }
    
    /// <summary>
    /// 中键点击 - 暂停/继续播放
    /// </summary>
    private void OnMiddleClick()
    {
        if (musicPlaylist.Count == 0) return;
        
        if (_isPlaying)
        {
            Pause();
            Debug.Log("[收音机] 暂停播放");
        }
        else
        {
            if (_audioSource.clip == null || _currentTrackIndex == -1)
            {
                PlayTrack(0);
            }
            else
            {
                Resume();
            }
            Debug.Log("[收音机] 继续播放");
        }
    }
    
    // ── 播放控制 ────────────────────────────────────────────────────────────
    
    private void SetupAudioSource()
    {
        _audioSource.loop = false;
        _audioSource.playOnAwake = false;
        _audioSource.rolloffMode = AudioRolloffMode.Custom;
        _audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, distanceRolloff);
        _audioSource.maxDistance = maxDistance;
        _audioSource.spatialBlend = 1f;  // 3D音效
        _audioSource.volume = masterVolume;
    }
    
    /// <summary>播放指定索引的歌曲</summary>
    public void PlayTrack(int index)
    {
        if (musicPlaylist.Count == 0) return;
        
        index = Mathf.Clamp(index, 0, musicPlaylist.Count - 1);
        
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);
        
        _currentTrackIndex = index;
        AudioClip newClip = musicPlaylist[_currentTrackIndex];
        
        if (_audioSource.isPlaying && _audioSource.clip != newClip)
        {
            _fadeCoroutine = StartCoroutine(FadeOutAndSwitch(newClip));
        }
        else if (!_audioSource.isPlaying)
        {
            _audioSource.clip = newClip;
            _audioSource.volume = masterVolume;
            _audioSource.Play();
            _isPlaying = true;
            StartTrackEndWatcher();
            _fadeCoroutine = StartCoroutine(FadeIn());
        }
        
        UpdateDisplay();
    }
    
    /// <summary>下一首</summary>
    public void NextTrack()
    {
        if (musicPlaylist.Count == 0) return;
        
        int nextIndex = _currentTrackIndex + 1;
        if (nextIndex >= musicPlaylist.Count)
        {
            if (loopPlaylist)
                nextIndex = 0;
            else
            {
                Stop();
                return;
            }
        }
        
        PlayTrack(nextIndex);
    }
    
    /// <summary>上一首</summary>
    public void PreviousTrack()
    {
        if (musicPlaylist.Count == 0) return;
        
        int prevIndex = _currentTrackIndex - 1;
        if (prevIndex < 0)
        {
            if (loopPlaylist)
                prevIndex = musicPlaylist.Count - 1;
            else
            {
                PlayTrack(0);
                return;
            }
        }
        
        PlayTrack(prevIndex);
    }
    
    /// <summary>暂停播放</summary>
    public void Pause()
    {
        if (_isPlaying && _audioSource.isPlaying)
        {
            _audioSource.Pause();
            _isPlaying = false;
            StopTrackEndWatcher();
            UpdateDisplay();
        }
    }
    
    /// <summary>恢复播放</summary>
    public void Resume()
    {
        if (!_isPlaying && _audioSource.clip != null)
        {
            _audioSource.UnPause();
            _isPlaying = true;
            StartTrackEndWatcher();
            UpdateDisplay();
        }
    }
    
    /// <summary>停止播放（重置到开头）</summary>
    public void Stop()
    {
        _audioSource.Stop();
        _isPlaying = false;
        StopTrackEndWatcher();
        UpdateDisplay();
    }
    
    /// <summary>设置音量</summary>
public void SetVolume(float volume)
{
    masterVolume = Mathf.Clamp01(volume);
    if (_fadeCoroutine == null)
        _audioSource.volume = masterVolume;
}
    
    // ── UI 显示 ────────────────────────────────────────────────────────────
    
    private void UpdateDisplay()
    {
        string displayString;
        
        if (musicPlaylist.Count == 0 || _currentTrackIndex < 0)
        {
            displayString = noMusicText;
        }
        else
        {
            string songName = GetSongName(_currentTrackIndex);
            string statusSuffix = _isPlaying ? playingSuffix : pausedSuffix;
            displayString = string.Format(displayFormat, songName + statusSuffix, 
                                         _currentTrackIndex + 1, musicPlaylist.Count);
        }

        if (_lastDisplayString == displayString)
            return;

        _lastDisplayString = displayString;
        
        if (displayTextMeshPro != null)
            displayTextMeshPro.text = displayString;
        
        if (displayText != null)
            displayText.text = displayString;
    }
    
    private string GetSongName(int index)
    {
        if (index < 0 || index >= musicPlaylist.Count)
            return "???";
        
        if (songNames != null && index < songNames.Count && !string.IsNullOrEmpty(songNames[index]))
            return songNames[index];
        
        if (musicPlaylist[index] != null)
            return musicPlaylist[index].name;
        
        return $"Track {index + 1}";
    }
    
    // ── 淡入淡出 ───────────────────────────────────────────────────────────
    
    private System.Collections.IEnumerator FadeOutAndSwitch(AudioClip newClip)
    {
        float elapsed = 0f;
        float startVolume = _audioSource.volume;
        
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            _audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeTime);
            yield return null;
        }
        
        _audioSource.Stop();
        _audioSource.clip = newClip;
        _audioSource.volume = 0f;
        _audioSource.Play();
        _isPlaying = true;
        StartTrackEndWatcher();
        
        elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            _audioSource.volume = Mathf.Lerp(0f, masterVolume, elapsed / fadeTime);
            yield return null;
        }
        
        _audioSource.volume = masterVolume;
        _fadeCoroutine = null;
        UpdateDisplay();
    }

    private void StartTrackEndWatcher()
    {
        if (_trackEndCoroutine != null)
            StopCoroutine(_trackEndCoroutine);

        _trackEndCoroutine = StartCoroutine(TrackEndWatcher());
    }

    private void StopTrackEndWatcher()
    {
        if (_trackEndCoroutine == null)
            return;

        StopCoroutine(_trackEndCoroutine);
        _trackEndCoroutine = null;
    }
    
    private System.Collections.IEnumerator FadeIn()
    {
        float elapsed = 0f;
        _audioSource.volume = 0f;
        
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            _audioSource.volume = Mathf.Lerp(0f, masterVolume, elapsed / fadeTime);
            yield return null;
        }
        
        _audioSource.volume = masterVolume;
        _fadeCoroutine = null;
    }
    
    // ── 公共方法 ───────────────────────────────────────────────────────────
    
    /// <summary>刷新音乐列表</summary>
    public void RefreshPlaylist()
    {
        if (musicPlaylist.Count == 0)
            Stop();
        UpdateDisplay();
    }
    
    /// <summary>动态添加歌曲</summary>
    public void AddSong(AudioClip clip, string customName = null)
    {
        if (clip == null) return;
        
        musicPlaylist.Add(clip);
        if (!string.IsNullOrEmpty(customName))
        {
            while (songNames.Count < musicPlaylist.Count) songNames.Add("");
            songNames[musicPlaylist.Count - 1] = customName;
        }
        
        if (musicPlaylist.Count == 1 && autoPlayOnStart)
            PlayTrack(0);
    }
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 1, 0, 0.2f);
        Gizmos.DrawWireSphere(transform.position, maxDistance);
    }
}
