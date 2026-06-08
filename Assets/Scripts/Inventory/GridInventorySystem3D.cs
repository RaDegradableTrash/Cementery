using System.Collections.Generic;
using UnityEngine;

public class GridInventorySystem3D : MonoBehaviour
{
    [Header("背包立方体尺寸")]
    public int width = 6;  // X轴（横向宽度）
    public int height = 4; // Y轴（纵向高度/堆叠层数）
    public int length = 6; // Z轴（纵深长度）

    // 核心数据：三维阵列。如果某个坐标有物体，对应的位置就不为 null
    private ItemInstance3D[,,] grid;
    private List<ItemInstance3D> allItems = new List<ItemInstance3D>();

    void Awake()
    {
        grid = new ItemInstance3D[width, height, length];
    }

    /// <summary>
    /// 检查一个物品是否可以放入指定的 3D 坐标原点
    /// </summary>
    public bool CanPlaceItem(ItemData3D item, Vector3Int anchor)
    {
        foreach (Vector3Int offset in item.occupiedOffsets)
        {
            int x = anchor.x + offset.x;
            int y = anchor.y + offset.y;
            int z = anchor.z + offset.z;

            // 1. 边界检查：是否超出了背包的立方体边界
            if (x < 0 || x >= width || y < 0 || y >= height || z < 0 || z >= length)
                return false;

            // 2. 占用检查：该格子是否已经被其他物品占了
            if (grid[x, y, z] != null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 实际将物品塞进 3D 背包
    /// </summary>
    public bool InsertItem(ItemData3D item, Vector3Int anchor)
    {
        if (!CanPlaceItem(item, anchor)) return false;

        ItemInstance3D newInstance = new ItemInstance3D(item, anchor);

        // 在三维数组中登记占用
        foreach (Vector3Int offset in item.occupiedOffsets)
        {
            int x = anchor.x + offset.x;
            int y = anchor.y + offset.y;
            int z = anchor.z + offset.z;
            grid[x, y, z] = newInstance;
        }

        allItems.Add(newInstance);
        Debug.Log($"成功将 {item.itemName} 放入背包坐标: {anchor}");
        return true;
    }
}