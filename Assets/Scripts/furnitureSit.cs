using System.Collections;
using UnityEngine;

[RequireComponent(typeof(WorldObject))]
public class FurnitureObject : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform sitAnchor;
    [SerializeField] private Transform exitAnchor;

    [Header("Transition")]
    [SerializeField] private float transitionDuration = 0.3f;

    [Header("Input Settings")]
    [SerializeField] private KeyCode exitKey = KeyCode.BackQuote;

    private WorldObject _worldObject;
    private GameObject _currentActor;
    private bool _isOccupied = false;
    
    private Transform _actorOriginalParent;

    void Awake()
    {
        _worldObject = GetComponent<WorldObject>();
    }

    void OnEnable()
    {
        if (_worldObject != null) _worldObject.onInteract.AddListener(OnFurnitureInteracted);
    }

    void OnDisable()
    {
        if (_worldObject != null) _worldObject.onInteract.RemoveListener(OnFurnitureInteracted);
    }

    void Update()
    {
        if (_isOccupied && Input.GetKeyDown(exitKey))
        {
            StartCoroutine(ExitFurnitureCo());
        }
    }

    private void OnFurnitureInteracted(GameObject actor)
    {
        // 核心安全检查：如果已经有人占了，或者actor为空，直接返回
        if (_isOccupied || sitAnchor == null || actor == null) return;

        _currentActor = actor;
        _isOccupied = true;

        StartCoroutine(EnterFurnitureCo(actor));
    }

    IEnumerator EnterFurnitureCo(GameObject actor)
    {
        // 1. 【核心修改】在移动前，立刻、彻底剥夺玩家的物理和移动控制权
        ForceLockPlayerPhysics(actor, true);

        _actorOriginalParent = actor.transform.parent;
        
        Vector3 startPos = actor.transform.position;
        Quaternion startRot = actor.transform.rotation;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float percent = Mathf.Clamp01(elapsed / transitionDuration);
            float t = EaseInOut(percent);

            actor.transform.position = Vector3.Lerp(startPos, sitAnchor.position, t);
            actor.transform.rotation = Quaternion.Lerp(startRot, sitAnchor.rotation, t);
            yield return null;
        }

        // 确保精准对齐
        actor.transform.position = sitAnchor.position;
        actor.transform.rotation = sitAnchor.rotation;

        // 2. 成为子物体
        actor.transform.SetParent(sitAnchor);

        // 3. 【核心修改】到达目的地后，再次强制刷新一次底层坐标，防止部分Controller在协程结束后闪回
        var cc = actor.GetComponent<CharacterController>();
        if (cc != null)
        {
            // 这行代码会强制让 CharacterController 更新内部的底层 C++ 缓存坐标
            cc.Move(Vector3.zero); 
        }
    }

    IEnumerator ExitFurnitureCo()
    {
        if (_currentActor == null) yield break;

        _currentActor.transform.SetParent(_actorOriginalParent);

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

        // 4. 【核心修改】彻底恢复玩家控制
        ForceLockPlayerPhysics(_currentActor, false);

        // 5. 【核心修改】确保只有在完全安全退出后，才释放占用标记，允许下次交互
        _currentActor = null;
        _isOccupied = false;
    }

    /// <summary>
    /// 强力切断/恢复玩家底层的物理与脚本联系
    /// </summary>
    private void ForceLockPlayerPhysics(GameObject actor, bool isLock)
    {
        // 如果是 CharacterController 架构
        var cc = actor.GetComponent<CharacterController>();
        if (cc != null) 
        {
            cc.enabled = !isLock; // 锁定时禁用，解锁时开启
        }

        // 如果是 Rigidbody 架构
        var rb = actor.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = isLock;      // 锁定时不吃物理
            rb.velocity = Vector3.zero;   // 彻底清空冲量速度
            rb.angularVelocity = Vector3.zero;
        }

        // 如果有常规碰撞体
        var col = actor.GetComponent<Collider>();
        if (col != null && cc == null) // 有CC的话就不用单独关Collider，没有CC才关
        {
            col.enabled = !isLock;
        }

        // ─── 🛠️ 关键排查项 ──────────────────────────────────────────
        // 如果你的项目使用的是特定的玩家脚本（比如 PlayerMovement，TP_Controller等）
        // 请取消下方的注释，并把它们填进去：
        // var myMovement = actor.GetComponent<你的移动脚本类名>();
        // if(myMovement != null) myMovement.enabled = !isLock;
    }

    private float EaseInOut(float t) => t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
}