using UnityEngine;

/// <summary>
/// 档位按钮音效播放器接口
/// </summary>
public interface IGearAudioPlayer
{
    void PlayShiftSuccess(CarControl.GearMode gear);
    void PlayShiftFail(CarControl.GearMode gear);
}