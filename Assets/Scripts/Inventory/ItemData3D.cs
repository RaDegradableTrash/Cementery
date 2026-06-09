using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "3D背包/物品数据")]
public class ItemData3D : ScriptableObject
{
    public string itemName;
    
    [Header("3D方块占用偏移量")]
    [Tooltip("比如 (0,0,0) 代表 1x1x1 的方块；如果再加上 (0,1,0)，就代表高为 2 的长条物")]
    public List<Vector3Int> occupiedOffsets = new List<Vector3Int> { new Vector3Int(0, 0, 0) };

    public GameObject previewPrefab; // 放在背包里预览用的 3D 模型
}

// 物品被放入背包后的具体实例
public class ItemInstance3D
{
    public ItemData3D data;
    public Vector3Int anchor; // 被放进背包三维阵列中的原点坐标 (X, Y, Z)
    
    public ItemInstance3D(ItemData3D data, Vector3Int anchor)
    {
        this.data = data;
        this.anchor = anchor;
    }
}