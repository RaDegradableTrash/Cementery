using System.Collections.Generic;
using UnityEngine;

public class PremodeledContainer : MonoBehaviour
{
    [System.Serializable]
    public class ContainerSkinStage
    {
        [Tooltip("该模型对应的齿轮完美数量（如：空盒子为0，一槽成品盒子为1...）")]
        public int gearCount;
        [Tooltip("你在场景里做好的对应成品盒子物体（如 CabinetWithThreeGear4）")]
        public GameObject skinRootObject;
    }

    [Header("Acceptable Settings")]
    [Tooltip("此容器允许放入的物体 Prefab 列表（如项目资源里的 Gear4 预制体）")]
    public List<GameObject> allowedPrefabs;

    [Header("Visual Skin Stages (成品盒子外观配置)")]
    [Tooltip("请把你的 0~5 槽成品盒子按顺序拖进这里")]
    public List<ContainerSkinStage> skinStages;

    // 运行时仅记录当前存入的齿轮数量
    private int _currentGearCount = 0;
    private int _maxCapacity = 5;

    public bool IsEmpty => _currentGearCount == 0;
    public bool IsFull => _currentGearCount >= _maxCapacity;

    void Awake()
    {
        // 计算当前配置里的最大容量
        _maxCapacity = 0;
        foreach (var stage in skinStages)
        {
            if (stage.gearCount > _maxCapacity) _maxCapacity = stage.gearCount;
        }

        UpdateSkinVisuals();
    }

    /// <summary>
    /// 判断手里的物体是否可以放入此盒子
    /// </summary>
    public bool CanStore(WorldObject carriedWo, out GameObject matchedPrefab, out int itemSize)
    {
        matchedPrefab = null;
        itemSize = 1;

        if (carriedWo == null || IsFull) return false;

        // 获取物体的占用体积
        if (carriedWo.collectItemData != null && carriedWo.collectItemData.localOffsets != null)
        {
            itemSize = carriedWo.collectItemData.localOffsets.Count;
            if (itemSize == 0) itemSize = 1;
        }

        // 判断塞入后是否会超出这个收纳箱的最高上限
        if (_currentGearCount + itemSize > _maxCapacity) return false;

        // 检查物品是否在允许的列表中
        if (allowedPrefabs == null) return false;
        foreach (var prefab in allowedPrefabs)
        {
            if (prefab != null && carriedWo.gameObject.name.StartsWith(prefab.name))
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
    public bool TryInsert(GameObject prefab, int size)
    {
        if (_currentGearCount + size > _maxCapacity) return false;

        _currentGearCount += size;
        
        // 瞬间切换视觉材质或成品模型，零碰撞，绝对安全
        UpdateSkinVisuals();
        return true;
    }

    /// <summary>
    /// 取出物品
    /// </summary>
    public GameObject TryExtract(out GameObject originPrefab)
    {
        originPrefab = null;
        if (IsEmpty) return null;

        _currentGearCount--;
        UpdateSkinVisuals();

        // 默认返回允许放入的第一个预制体还给玩家的手
        if (allowedPrefabs != null && allowedPrefabs.Count > 0)
        {
            originPrefab = allowedPrefabs[0];
        }

        return originPrefab;
    }

    private void UpdateSkinVisuals()
    {
        if (skinStages == null || skinStages.Count == 0) return;

        foreach (var stage in skinStages)
        {
            if (stage.skinRootObject != null)
            {
                stage.skinRootObject.SetActive(stage.gearCount == _currentGearCount);
            }
        }
    }
}