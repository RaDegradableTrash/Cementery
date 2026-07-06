using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// 适用于床、椅子等家具的互动脚本。
/// 包含防闪回的玩家控制器硬拦截控制。
/// </summary>
public class FurnitureObject : NetworkBehaviour
{
    [Header("互动点配置")]
    [Tooltip("玩家躺下/坐下后，身体或相机对齐的目标点")]
    [SerializeField] private Transform targetAnchor;
    
    [Tooltip("玩家离开家具（下床/起立）后，安全放置玩家的位置点（防止卡进模型）")]
    [SerializeField] private Transform exitAnchor;

    [Header("平滑过渡设置")]
    [Tooltip("玩家移动到目标点所需的时间（秒）")]
    [SerializeField] private float transitionDuration = 0.8f;

    [Header("状态监控 (仅供面板预览)")]
    [SerializeField] private bool isOccupied = false;
    [SerializeField] private ulong occupyingClientId;

    private GameObject _currentActor;
    private PlayerController _currentController;
    private MouseLook _currentMouseLook;
    private Coroutine _activeCoroutine;

    private void Awake()
    {
        enabled = false;
    }

    /// <summary>
    /// 当玩家右键点击或按下互动键指向床时调用此方法
    /// </summary>
    /// <param name="playerGameObject">触发互动的玩家物体</param>
    public void Interact(GameObject playerGameObject)
    {
        // 如果床已经被占用，则无法互动
        if (isOccupied)
        {
            Debug.LogWarning($"[Furniture] {gameObject.name} 已被占用，无法使用。");
            return;
        }

        if (_activeCoroutine != null) return;

        // 开始上床/上椅子的平滑协程
        enabled = true;
        _activeCoroutine = StartCoroutine(EnterFurnitureCo(playerGameObject));
    }

    /// <summary>
    /// 当玩家躺在床上时，按下任意移动键（WASD）或跳跃键触发下床
    /// </summary>
    private void Update()
    {
        // 只有当前客户端占用了这个家具，且不是正在过渡的状态时，才监听下床输入
        if (!isOccupied || _currentActor == null) return;

        // 如果是网络游戏，只有本地所有者能决定自己下床
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (_currentController != null && !_currentController.IsOwner) return;
        }

        // 监听下床输入：检测到 WASD、方向键或空格键，则主动下床
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) || 
            Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D) || 
            Input.GetKeyDown(KeyCode.Space) ||
            Input.GetAxisRaw("Horizontal") != 0f || Input.GetAxisRaw("Vertical") != 0f)
        {
            if (_activeCoroutine == null)
            {
                _activeCoroutine = StartCoroutine(ExitFurnitureCo());
            }
        }
    }

    /// <summary>
    /// 上床（进入家具）的平滑过渡协程
    /// </summary>
    private IEnumerator EnterFurnitureCo(GameObject player)
    {
        _currentActor = player;
        _currentController = player.GetComponent<PlayerController>();
        
        if (Camera.main != null)
        {
            _currentMouseLook = Camera.main.GetComponent<MouseLook>();
        }

        // 🌟【核心修复第一步】上床前，立刻开启 PlayerController 的罢工硬拦截，清除所有残留速度
        if (_currentController != null)
        {
            _currentController.IsUsingFurniture = true; 
            _currentController.ResetVelocity();
            
            // 暂时关闭碰撞体，防止平滑移动时与床的碰撞箱发生物理推挤
            var col = player.GetComponent<CapsuleCollider>();
            if (col != null) col.enabled = false;
        }

        // 设置占用状态
        isOccupied = true;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && _currentController != null)
        {
            occupyingClientId = _currentController.OwnerClientId;
        }

        Vector3 startPos = player.transform.position;
        Quaternion startRot = player.transform.rotation;

        Vector3 targetPos = targetAnchor != null ? targetAnchor.position : transform.position;
        Quaternion targetRot = targetAnchor != null ? targetAnchor.rotation : transform.rotation;

        // 如果有视角控制，暂时锁住视角跟随
        if (_currentMouseLook != null) _currentMouseLook.enabled = false;

        // ── 平滑插值动画 ──
        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);

            player.transform.position = Vector3.Lerp(startPos, targetPos, t);
            player.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }

        // 确保精准对齐到目标点
        player.transform.position = targetPos;
        player.transform.rotation = targetRot;

        // 恢复视角控制，让玩家在躺着时依然能转动脖子看四周
        if (_currentMouseLook != null)
        {
            _currentMouseLook.enabled = true;
            // 允许围绕床的朝向进行视角的上下左右调整
            _currentMouseLook.SetBaseRotation(targetRot); 
        }

        _activeCoroutine = null;
        Debug.Log($"[Furniture] 玩家成功躺在/坐在了 {gameObject.name} 上。按下任意移动键可起立。");
    }

    /// <summary>
    /// 下床（离开家具）的平滑过渡协程
    /// </summary>
    private IEnumerator ExitFurnitureCo()
    {
        if (_currentActor == null)
        {
            _activeCoroutine = null;
            enabled = false;
            yield break;
        }

        Debug.Log("[Furniture] 正在从家具上起立...");

        // 锁定视角，防止玩家在起立移动过程中乱晃镜头导致位移偏离
        if (_currentMouseLook != null) _currentMouseLook.enabled = false;

        Vector3 startPos = _currentActor.transform.position;
        Quaternion startRot = _currentActor.transform.rotation;

        // 如果没有配置下床点，则默认在原位置起立
        Vector3 exitPos = exitAnchor != null ? exitAnchor.position : startPos;
        // 保持起立时玩家只在 Y 轴旋转（直立面）
        Quaternion exitRot = exitAnchor != null ? Quaternion.Euler(0f, exitAnchor.eulerAngles.y, 0f) : Quaternion.Euler(0f, startRot.eulerAngles.y, 0f);

        // ── 平滑离开动画 ──
        float elapsed = 0f;
        while (elapsed < transitionDuration * 0.8f) // 离开可以稍微快一点
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / (transitionDuration * 0.8f));

            _currentActor.transform.position = Vector3.Lerp(startPos, exitPos, t);
            _currentActor.transform.rotation = Quaternion.Slerp(startRot, exitRot, t);
            yield return null;
        }

        // 确保玩家完全到达安全的下床点位置
        _currentActor.transform.position = exitPos;
        _currentActor.transform.rotation = exitRot;

        // 恢复玩家的物理碰撞体
        var col = _currentActor.GetComponent<CapsuleCollider>();
        if (col != null) col.enabled = true;

        // 🌟【核心修复第二步】当且仅当玩家完全到达下床点后，才解除控制器的物理拦截
        if (_currentController != null)
        {
            _currentController.ResetVelocity();
            _currentController.IsUsingFurniture = false; // 彻底恢复移动能力
        }

        // 恢复视角控制，并将主相机的基础方向同步为当前角色直立的方向
        if (_currentMouseLook != null)
        {
            _currentMouseLook.enabled = true;
            _currentMouseLook.SetBaseRotation(exitRot);
        }

        // 重置家具自身状态，迎接下一次交互
        isOccupied = false;
        occupyingClientId = 0;
        _currentActor = null;
        _currentController = null;
        _currentMouseLook = null;
        _activeCoroutine = null;
        enabled = false;

        Debug.Log("[Furniture] 玩家已成功起立并安全回到地面。");
    }
}
