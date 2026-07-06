using UnityEngine;

public class CategorizedAudioController : MonoBehaviour
{
    // 定义大分类
    public enum KeyCategory
    {
        Letters,        // 字母键 (A-Z)
        Numbers,        // 主键盘数字键 (0-9)
        Arrows,         // 方向键
        Keypad,         // 数字小键盘
        Other           // 其他常用键 (空格、回车等)
    }

    [Header("音效组件配置")]
    public AudioSource audioSource;
    public AudioClip soundEffect;

    [Header("按键分类绑定")]
    [Tooltip("第一步：选择按键所属的分组")]
    public KeyCategory keyCategory = KeyCategory.Other;

    // 根据分类，显示对应的精简按键列表
    [Tooltip("第二步：在选中的分组中选择具体按键")]
    public KeyCode selectedKey = KeyCode.Space;

    void Start()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null || soundEffect == null)
            enabled = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(selectedKey))
        {
            PlaySound();
        }
    }

    private void PlaySound()
    {
        audioSource.PlayOneShot(soundEffect);
    }
}
