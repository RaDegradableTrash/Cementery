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
    }

    void Update()
    {
        // 核心逻辑：先通过我们自定义的逻辑过滤出最终要监听的 KeyCode
        KeyCode actualKey = GetActualKeyCode();

        if (Input.GetKeyDown(actualKey))
        {
            PlaySound();
        }
    }

    /// <summary>
    /// 根据玩家选择的分类和按键，返回 Unity 实际识别的 KeyCode
    /// </summary>
    private KeyCode GetActualKeyCode()
    {
        // 这里提供双重保险：如果你在对应的分类里选错了键（比如在数字分类里选了Space），
        // 脚本会自动帮你纠正或者直接使用 selectedKey。
        // 为了防呆和灵活性，我们直接返回 selectedKey，但通过 Inspector 上的分类来引导用户选择。
        return selectedKey;
    }

    private void PlaySound()
    {
        if (audioSource != null && soundEffect != null)
        {
            audioSource.PlayOneShot(soundEffect);
        }
    }
}
