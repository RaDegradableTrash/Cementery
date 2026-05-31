using System.Collections.Generic;
using UnityEngine;

public class PremodeledContainer : MonoBehaviour
{
    [System.Serializable]
    public class RuntimeStoredItem
    {
        public GameObject spawnedVisual;   // 留在盒子里的纯视觉网格
        public GameObject originPrefab;    // 原始 Prefab 资产，用于取出时还给手
        public int size;                    // 占用的体积（默认1）
    }

    [Header("Acceptable Settings")]
    [Tooltip("此容器允许放入的物体 Prefab 列表（如项目资源里的 Gear4 预制体）")]
    public List<GameObject> allowedPrefabs;

    [Header("Slot References (核心：只认这 5 个点)")]
    [Tooltip("直接把当前这一个盒子底下的 GearSpot1 ~ GearSpot5 拖进这里！")]
    public List<Transform> placementSlots;

    [Header("Animation Settings")]
    public float transitionDuration = 0.22f;
    public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // 运行时严格记录当前盒子里存进去的齿轮
    private List<RuntimeStoredItem> _storedItems = new List<RuntimeStoredItem>();
    private int _occupiedSlotCount = 0;

    public bool IsEmpty => _storedItems.Count == 0;
    public int RemainingSlots => placementSlots.Count - _occupiedSlotCount;

    /// <summary>
    /// 判断手里的物体是否可以放入此盒子
    /// </summary>
    public bool CanStore(WorldObject carriedWo, out GameObject matchedPrefab, out int itemSize)
    {
        matchedPrefab = null;
        itemSize = 1;

        if (carriedWo == null) return false;

        // 获取物体的占用体积
        if (carriedWo.collectItemData != null && carriedWo.collectItemData.localOffsets != null)
        {
            itemSize = carriedWo.collectItemData.localOffsets.Count;
            if (itemSize == 0) itemSize = 1;
        }

        // 检查剩余槽位是否足够
        if (RemainingSlots < itemSize) return false;

        if (allowedPrefabs == null || allowedPrefabs.Count == 0) return false;

        // 宽松的名字模糊配对，防止 (Clone) 后缀干扰
        string carriedName = carriedWo.gameObject.name.ToLower().Replace("(clone)", "").Trim();
        foreach (var prefab in allowedPrefabs)
        {
            if (prefab == null) continue;
            string prefabName = prefab.name.ToLower().Trim();

            if (carriedName.StartsWith(prefabName) || prefabName.StartsWith(carriedName))
            {
                matchedPrefab = prefab;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 放入物品
    /// </summary>
    public bool TryInsert(GameObject prefab, int size, Vector3 pickupScale, Vector3 currentWorldPos, Quaternion currentWorldRot)
    {
        if (RemainingSlots < size) return false;

        int targetSlotIndex = _occupiedSlotCount;
        if (targetSlotIndex >= placementSlots.Count || targetSlotIndex + size > placementSlots.Count) return false;

        Transform targetSpot = placementSlots[targetSlotIndex];

        // 1. 生成纯视觉体（先留在世界坐标作为动画起点）
        GameObject newVisual = Instantiate(prefab, currentWorldPos, currentWorldRot);
        
        // 🌟 降维打击：直接剥离所有碰撞体和刚体，让它变成纯粹老实的“幽灵渲染网格”，绝不引发物理崩溃！
        StripAllPhysicsComponents(newVisual);

        _storedItems.Add(new RuntimeStoredItem
        {
            spawnedVisual = newVisual,
            originPrefab = prefab,
            size = size
        });

        _occupiedSlotCount += size;

        // 2. 飞入对应的 Slot 点
        if (newVisual != null && targetSpot != null)
        {
            StartCoroutine(AnimateToSpotCo(newVisual, targetSpot, pickupScale));
        }

        return true;
    }

    /// <summary>
    /// 取出物品
    /// </summary>
    public GameObject TryExtract(out GameObject originPrefab)
    {
        originPrefab = null;
        if (IsEmpty) return null;

        int lastIndex = _storedItems.Count - 1;
        RuntimeStoredItem lastItem = _storedItems[lastIndex];
        _storedItems.RemoveAt(lastIndex);

        _occupiedSlotCount -= lastItem.size;
        originPrefab = lastItem.originPrefab;

        if (lastItem.spawnedVisual != null)
        {
            if (Application.isPlaying) Destroy(lastItem.spawnedVisual);
            else UnityEngine.Object.DestroyImmediate(lastItem.spawnedVisual);
        }

        return originPrefab;
    }

    private System.Collections.IEnumerator AnimateToSpotCo(GameObject visual, Transform targetSpot, Vector3 keepScale)
    {
        float elapsed = 0f;
        Vector3 startPos = visual.transform.position;
        Quaternion startRot = visual.transform.rotation;

        while (elapsed < transitionDuration)
        {
            if (visual == null || targetSpot == null) yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);
            float evaluatedT = transitionCurve.Evaluate(t);

            visual.transform.position = Vector3.Lerp(startPos, targetSpot.position, evaluatedT);
            visual.transform.rotation = Quaternion.Slerp(startRot, targetSpot.rotation, evaluatedT);
            visual.transform.localScale = keepScale;

            yield return null;
        }

        if (visual != null && targetSpot != null)
        {
            visual.transform.SetParent(targetSpot);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = keepScale;
        }
    }

    private void StripAllPhysicsComponents(GameObject obj)
    {
        // 强制摧毁刚体，防止警告
        Rigidbody rb = obj.GetComponent<Rigidbody>() ?? obj.GetComponentInParent<Rigidbody>();
        if (rb != null)
        {
            if (Application.isPlaying) Destroy(rb);
            else UnityEngine.Object.DestroyImmediate(rb);
        }

        // 强制关闭/摧毁所有层级的碰撞箱，防止 Non-convex MeshCollider 报错掐断执行流
        foreach (var col in obj.GetComponentsInChildren<Collider>(true))
        {
            if (Application.isPlaying) Destroy(col);
            else UnityEngine.Object.DestroyImmediate(col);
        }

        WorldObject wo = obj.GetComponent<WorldObject>() ?? obj.GetComponentInParent<WorldObject>();
        if (wo != null)
        {
            if (Application.isPlaying) Destroy(wo);
            else UnityEngine.Object.DestroyImmediate(wo);
        }
    }
}