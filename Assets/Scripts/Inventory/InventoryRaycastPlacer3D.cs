using UnityEngine;

public class InventoryRaycastPlacer3D : MonoBehaviour
{
    [Header("引用设置")]
    public Camera inventoryCamera;             // 渲染背包的独立相机
    public GridInventorySystem3D invSystem;    // 三维背包系统
    public ItemData3D testingItem;             // 测试用的物品数据

    [Header("格子物理大小")]
    public float cellSize = 1f;                // 每个 3D 立方体格子的世界物理尺寸

    private GameObject previewInstance;        // 当前生成的预览模型
    private Vector3Int currentHoverCell = new Vector3Int(-1, -1, -1);
    private bool isPreviewValid = false;

    // 隐形格子的辅助脚本
    public class CellTriggerInfo : MonoBehaviour
    {
        public Vector3Int cellCoordinate;
    }

    void Start()
    {
        GeneratePhysicsGridTriggers();
        if (testingItem != null) CreatePreview(testingItem);
    }

    /// <summary>
    /// 核心创新：在背包空间里生成一圈隐形 Collider，用来作为射线的“肉垫”
    /// </summary>
    void GeneratePhysicsGridTriggers()
    {
        GameObject gridRoot = new GameObject("3D_Grid_Triggers");
        gridRoot.transform.SetParent(this.transform, false);

        for (int x = 0; x < invSystem.width; x++)
        {
            for (int y = 0; y < invSystem.height; y++)
            {
                for (int z = 0; z < invSystem.length; z++)
                {
                    GameObject cellCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cellCube.name = $"Cell_{x}_{y}_{z}";
                    cellCube.transform.SetParent(gridRoot.transform, false);
                    
                    // 计算每个格子在 3D 空间里的物理对齐位置
                    cellCube.transform.localPosition = new Vector3(x * cellSize, y * cellSize, z * cellSize);
                    cellCube.transform.localScale = Vector3.one * cellSize * 0.98f; // 稍微缩小一点防止碰撞边界挤压

                    // 变成隐形触发器
                    cellCube.GetComponent<MeshRenderer>().enabled = false; // 隐形
                    cellCube.GetComponent<BoxCollider>().isTrigger = true;

                    // 绑定坐标数据
                    var info = cellCube.AddComponent<CellTriggerInfo>();
                    info.cellCoordinate = new Vector3Int(x, y, z);
                }
            }
        }
    }

    void Update()
    {
        HandleRaycast();
        HandlePlacementInput();
    }

    /// <summary>
    /// 每帧执行：鼠标发出的射线去射击我们刚才生成的隐形方块墙
    /// </summary>
    void HandleRaycast()
    {
        if (previewInstance == null || testingItem == null) return;

        Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
        
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            CellTriggerInfo cellInfo = hit.collider.GetComponent<CellTriggerInfo>();
            if (cellInfo != null)
            {
                // 1. 成功拿到了鼠标此时指向的背包内部 [X,Y,Z] 坐标！
                currentHoverCell = cellInfo.cellCoordinate;

                // 2. 将 3D 预览模型的物理位置，直接吸附到这个隐形格子的位置
                previewInstance.transform.position = hit.collider.transform.position;

                // 3. 验证当前位置是否可以放下
                isPreviewValid = invSystem.CanPlaceItem(testingItem, currentHoverCell);

                // 4. 变色反馈：能放变绿，不能放变红
                SetPreviewColor(isPreviewValid ? Color.green : Color.red);
                return;
            }
        }

        // 如果射线什么都没射中，把状态重置
        currentHoverCell = new Vector3Int(-1, -1, -1);
        isPreviewValid = false;
        SetPreviewColor(Color.red);
    }

    void HandlePlacementInput()
    {
        if (Input.GetMouseButtonDown(0) && isPreviewValid && currentHoverCell.x != -1)
        {
            // 在逻辑层真正塞入数据
            if (invSystem.InsertItem(testingItem, currentHoverCell))
            {
                // 简单实现：成功放下后，把当前的预览模型“钉死”在背包里作为固定实体
                previewInstance = null; 
                
                // 重新派发下一个测试物品（如果在真实游戏里，这里应该去获取手里的下一个东西）
                if (testingItem != null) CreatePreview(testingItem);
            }
        }
    }

    void CreatePreview(ItemData3D item)
    {
        if (item.previewPrefab != null)
        {
            previewInstance = Instantiate(item.previewPrefab);
            // 确保预览物体的轴心在左下角，或者大小合适
        }
    }

    void SetPreviewColor(Color col)
    {
        if (previewInstance == null) return;
        Renderer[] rends = previewInstance.GetComponentsInChildren<Renderer>();
        foreach (var r in rends)
        {
            r.material.color = new Color(col.r, col.g, col.b, 0.5f);
        }
    }
}