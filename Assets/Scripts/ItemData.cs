using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物品数据（形状、属性、旋转）
/// </summary>
[CreateAssetMenu(menuName = "Inventory/ItemData")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public List<Vector3Int> localOffsets; // 形状偏移
    public float weight;
    public bool fragile;
    public Material material;
    public GameObject previewPrefab; // 预览用Prefab
    public GameObject worldPrefab; // 真实世界用Prefab（用于爆装备/掉落）
    [HideInInspector] public Vector3 savedWorldScale = Vector3.zero;

    // 旋转后返回所有占用格子的偏移
    public IEnumerable<Vector3Int> GetRotatedOffsets(Quaternion rotation)
    {
        foreach (var offset in localOffsets)
        {
            Vector3 rotated = rotation * offset;
            yield return new Vector3Int(Mathf.RoundToInt(rotated.x), Mathf.RoundToInt(rotated.y), Mathf.RoundToInt(rotated.z));
        }
    }
}

/// <summary>
/// 物品实例（引用数据、锚点、旋转）
/// </summary>
public class ItemInstance
{
    public ItemData item;
    public Vector3Int anchor;
    public Quaternion rotation;
    public ItemInstance(ItemData item, Vector3Int anchor, Quaternion rotation)
    {
        this.item = item;
        this.anchor = anchor;
        this.rotation = rotation;
    }
}
