using System.Collections;
using UnityEngine;

[RequireComponent(typeof(WorldObject))]
public class FurnitureObject : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("角色坐下/躺下后的目标位置和旋转锚点（场景中的空物体）")]
    [SerializeField] private Transform sitAnchor;
    
    [Tooltip("角色回到正常站立状态的离开锚点（可选，如果不填则默认在原地站起）")]
    [SerializeField] private Transform exitAnchor;

    [Header("Transition")]
    [Tooltip("角色移动到家具位置的过渡时间（秒）")]
    [SerializeField] private float transitionDuration = 0.3f;

    [Header("Input Settings")]
    [Tooltip("用来脱离家具的按键，默认是 Tab 下面的 ` 键（BackQuote）")]
    [SerializeField] private KeyCode exitKey = KeyCode.BackQuote;

    private WorldObject _worldObject;
    private GameObject _currentActor;
    private bool _isOccupied = false;
    
    // 用于恢复角色原本状态的备份变量
    private Vector3 _originalActorScale;
    private Transform _actorOriginalParent;

    void Awake()
    {
        _worldObject = GetComponent<WorldObject>();
        
        if (sitAnchor == null)
        {
            Debug.LogError($"[FurnitureObject] {gameObject.name} 缺少坐下锚点(Sit Anchor)！", this);
        }
    }

    void OnEnable()
    {
        // 绑定到 WorldObject 的 UnityEvent
        if (_worldObject != null)
        {
            _worldObject.onInteract.AddListener(OnFurnitureInteracted);
        }
    }

    void OnDisable()
    {
        if (_worldObject != null)
        {
            _worldObject.onInteract.RemoveListener(OnFurnitureInteracted);
        }
    }

    void Update()
    {
        // 如果有人在坐着，并且按下了脱离键
        if (_isOccupied && Input.GetKeyDown(exitKey))
        {
            StartCoroutine(ExitFurnitureCo());
        }
    }

    private void OnFurnitureInteracted(GameObject actor)
    {
        if (_isOccupied || sitAnchor == null || actor == null) return;

        _currentActor = actor;
        _isOccupied = true;

        StartCoroutine(EnterFurnitureCo(actor));
    }

    /// <summary>
    /// 模拟坐下/躺下的平滑过渡协程
    /// </summary>
    IEnumerator EnterFurnitureCo(GameObject actor)
    {
        // 1. 禁用角色的移动脚本（请根据你自己的项目替换 CharacterController 或 PlayerMovement 脚本名）
        TogglePlayerControl(actor, false);

        // 备份原本的父物体
        _actorOriginalParent = actor.transform.parent;
        
        Vector3 startPos = actor.transform.position;
        Quaternion startRot = actor.transform.rotation;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float percent = Mathf.Clamp01(elapsed / transitionDuration);
            
            // 使用和平滑动画一致的 EaseInOut 曲线
            float t = EaseInOut(percent);

            actor.transform.position = Vector3.Lerp(startPos, sitAnchor.position, t);
            actor.transform.rotation = Quaternion.Lerp(startRot, sitAnchor.rotation, t);
            yield return null;
        }

        // 确保最终精准对齐
        actor.transform.position = sitAnchor.position;
        actor.transform.rotation = sitAnchor.rotation;

        // 2. 将角色设置为锚点（或家具）的子物体，这样家具移动时角色也会跟着动
        actor.transform.SetParent(sitAnchor);
    }

    /// <summary>
    /// 脱离家具的平滑过渡协程
    /// </summary>
    IEnumerator ExitFurnitureCo()
    {
        if (_currentActor == null) yield break;

        // 解除父子关系，恢复到角色原本的父级
        _currentActor.transform.SetParent(_actorOriginalParent);

        // 确定起立的目标位置（如果有脱离点就去脱离点，没有就在原地起立）
        Vector3 targetPos = exitAnchor != null ? exitAnchor.position : _currentActor.transform.position;
        Quaternion targetRot = exitAnchor != null ? exitAnchor.rotation : _currentActor.transform.rotation;

        Vector3 startPos = _currentActor.transform.position;
        Quaternion startRot = _currentActor.transform.rotation;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float percent = Mathf.Clamp01(elapsed / transitionDuration);
            float t = EaseInOut(percent);

            _currentActor.transform.position = Vector3.Lerp(startPos, targetPos, t);
            _currentActor.transform.rotation = Quaternion.Lerp(startRot, targetRot, t);
            yield return null;
        }

        _currentActor.transform.position = targetPos;
        _currentActor.transform.rotation = targetRot;

        // 3. 恢复角色的移动控制
        TogglePlayerControl(_currentActor, true);

        // 清空状态
        _currentActor = null;
        _isOccupied = false;
    }

    /// <summary>
    /// 控制角色脚本的开关（只锁移动，不锁转头）
    /// </summary>
    private void TogglePlayerControl(GameObject actor, bool enable)
    {
        // 方案 A: 如果你使用的是 Unity 官方的 CharacterController
        var cc = actor.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = enable;

        // 方案 B: 禁用你自己的玩家移动控制脚本（此处需要修改为你的实际移动脚本类名，例如 PlayerController）
        // var movement = actor.GetComponent<PlayerMovement>();
        // if (movement != null) movement.enabled = enable;
        
        // 💡 注意：请确保你的视角旋转（转头）脚本和移动脚本是分离的。
        // 这样这里只禁用了移动脚本，视角旋转（如 MouseLook）依然保持工作，就能实现“能转头不能移动”的效果。
    }

    // 复用你原代码中的平滑曲线
    private float EaseInOut(float t) => t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
}