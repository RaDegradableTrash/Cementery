using UnityEngine;

public class AutoAddColliders : MonoBehaviour
{
    [ContextMenu("一键生成网格碰撞体")]
    public void GenerateMeshColliders()
    {
        int count = 0;
        // 遍历所有带有 MeshFilter 的子物体
        MeshFilter[] meshes = GetComponentsInChildren<MeshFilter>();
        foreach (MeshFilter mf in meshes)
        {
            // 如果它还没有碰撞体，就给它加一个
            if (mf.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                // 动态物体必须勾选 Convex，否则物理引擎会报错
                mc.convex = true; 
                count++;
            }
        }
        Debug.Log($"完工！一共为 {count} 个零件添加了碰撞体！");
    }
}
