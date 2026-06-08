using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 3D空间交互背包系统核心：网格数据、物品放置、旋转、重叠检测
/// 适配房车/温室等空间解谜玩法
/// </summary>
public class GridInventorySystem : MonoBehaviour
{
    [Header("Grid Volume Size")] // 背包空间尺寸
    public int gridWidth = 3;
    public int gridHeight = 4;
    public int gridDepth = 2;

    // 三维数组存储物品实例
    private ItemInstance[,,] grid;

    // 当前操作层级（Y轴）
    public int currentLayer = 0;

    public event System.Action<ItemInstance> OnItemPlaced;
    public event System.Action<ItemInstance> OnItemRemoved;

    private readonly List<ItemInstance> allItems = new List<ItemInstance>();

    void Awake()
    {
        // 如果是旧的默认尺寸(6, 4, 6)，则自动迁移到新的默认尺寸长2(Depth=2) 宽3(Width=3) 高4(Height=4)
        if (gridWidth == 6 && gridHeight == 4 && gridDepth == 6)
        {
            gridWidth = 3;
            gridHeight = 4;
            gridDepth = 2;
        }
        grid = new ItemInstance[gridWidth, gridHeight, gridDepth];
    }

    /// <summary>
    /// 检查物品在指定位置和旋转下是否可以放置（不重叠、不越界）
    /// </summary>
    public bool CanPlace(ItemData item, Vector3Int anchor, Quaternion rotation)
    {
        foreach (var offset in item.GetRotatedOffsets(rotation))
        {
            Vector3Int pos = anchor + offset;
            if (!InBounds(pos)) return false;
            if (grid[pos.x, pos.y, pos.z] != null) return false;
        }
        return true;
    }

    /// <summary>
    /// 放置物品到指定位置和旋转
    /// </summary>
    public bool Place(ItemData item, Vector3Int anchor, Quaternion rotation)
    {
        if (!CanPlace(item, anchor, rotation)) return false;
        var instance = new ItemInstance(item, anchor, rotation);
        foreach (var offset in item.GetRotatedOffsets(rotation))
        {
            Vector3Int pos = anchor + offset;
            grid[pos.x, pos.y, pos.z] = instance;
        }
        
        allItems.Add(instance);
        OnItemPlaced?.Invoke(instance);
        
        return true;
    }

    /// <summary>
    /// 移除指定锚点的物品
    /// </summary>
    public void Remove(Vector3Int anchor)
    {
        var inst = grid[anchor.x, anchor.y, anchor.z];
        if (inst == null) return;
        foreach (var offset in inst.item.GetRotatedOffsets(inst.rotation))
        {
            Vector3Int pos = inst.anchor + offset;
            if (InBounds(pos) && grid[pos.x, pos.y, pos.z] == inst)
                grid[pos.x, pos.y, pos.z] = null;
        }
        
        allItems.Remove(inst);
        OnItemRemoved?.Invoke(inst);
    }

    /// <summary>
    /// 判断坐标是否在背包范围内
    /// </summary>
    public bool InBounds(Vector3Int pos)
    {
        return pos.x >= 0 && pos.x < gridWidth &&
               pos.y >= 0 && pos.y < gridHeight &&
               pos.z >= 0 && pos.z < gridDepth;
    }

    /// <summary>
    /// 判断某个格子是否已被占用；越界视为占用（不可放置）。
    /// </summary>
    public bool IsOccupied(Vector3Int pos)
    {
        if (!InBounds(pos))
            return true;

        return grid[pos.x, pos.y, pos.z] != null;
    }

    /// <summary>
    /// 获取指定坐标处的物品实例，如果没有则返回null
    /// </summary>
    public ItemInstance GetItemAt(Vector3Int pos)
    {
        if (!InBounds(pos)) return null;
        return grid[pos.x, pos.y, pos.z];
    }
    
    /// <summary>
    /// 获取背包中所有放置的物品
    /// </summary>
    public IReadOnlyList<ItemInstance> GetAllItems()
    {
        return allItems;
    }

    // 可扩展：序列化存档、物理属性、层级高亮等
}
