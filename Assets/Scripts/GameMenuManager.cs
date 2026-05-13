using UnityEngine;

public class GameMenuManager : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject menuPanel;
    
    public static bool IsMenuOpen = false;

    void Awake()
    {
        IsMenuOpen = false;
    }

    void Start()
    {
        // 初始关闭菜单，锁定鼠标
        CloseMenu();
    }

    void Update()
    {
        // 监听 ESC 键
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
        {
            // 死亡期间禁止打开菜单
            if (PlayerDeathFlowController.IsPlayerDead) return;
            
            Debug.Log("GameMenuManager: Detected ESC or P press. Current IsMenuOpen state: " + IsMenuOpen);
            
            if (menuPanel == null)
            {
                Debug.LogError("GameMenuManager: menuPanel is NULL! Please assign it in the Inspector.");
                return;
            }

            if (IsMenuOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }
    }

    public void OpenMenu()
    {
        IsMenuOpen = true;
        menuPanel.SetActive(true);
        
        // 解锁鼠标
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 注意：联机游戏中通常不建议使用 Time.timeScale = 0，因为会导致网络同步问题。
        // 如果你希望单机模式下可以暂停，可以开启下面这行：
        // if (!Unity.Netcode.NetworkManager.Singleton.IsClient && !Unity.Netcode.NetworkManager.Singleton.IsServer) Time.timeScale = 0;
    }

    public void CloseMenu()
    {
        IsMenuOpen = false;
        menuPanel.SetActive(false);
        
        // 重新锁定鼠标（如果你有第一人称控制的话）
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 恢复时间
        // Time.timeScale = 1;
    }

    // 供 UI 上的“返回游戏”按钮调用
    public void ResumeGame()
    {
        CloseMenu();
    }
}
