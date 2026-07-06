using UnityEngine;
using TMPro;

public class FuelCan : MonoBehaviour
{
    [Header("Fuel Settings")]
    [SerializeField] private float fuelAmount = 100f; // 油桶内剩余燃油量（0-100）
    [SerializeField] private float maxFuelAmount = 100f;
    
    [Header("References")]
    [SerializeField] private TextMeshProUGUI fuelDisplay; // 油桶上的燃油显示
    [SerializeField] private float refillDistance = 2f; // 加油的有效距离
    [SerializeField] private LayerMask vehicleLayer = -1; // 车辆检测层
    
    [Header("Feedback")]
    [SerializeField] private GameObject refillEffect; // 加油特效（可选）
    [SerializeField] private string refillSound = "Refill"; // 加油音效名称（可选）
    [SerializeField] private bool logRefillDebug = false;
    
    private WorldObject worldObject;
    private CarControl targetCar; // 缓存目标车辆
    private Camera cachedMainCamera;
    private Transform cachedMainCameraTransform;
    private Transform fuelDisplayTransform;
    private float nextCameraLookupTime;
    private float nextDisplayFacingUpdateTime;
    private int lastDisplayedFuelPercent = int.MinValue;
    private Color lastDisplayColor;
    private static CarControl[] s_cachedCars;
    private static float s_nextCarCacheRefreshTime;
    private const float CarCacheRefreshInterval = 1f;
    
    public float FuelAmount => fuelAmount;
    public bool IsEmpty => fuelAmount <= 0f;
    
    void Awake()
    {
        worldObject = GetComponent<WorldObject>();
        if (worldObject == null)
        {
            worldObject = gameObject.AddComponent<WorldObject>();
        }
        
        // 配置 WorldObject 为可携带物品
        worldObject.carryable = true;
        worldObject.interactable = false;
        worldObject.collectable = false;
        worldObject.canBePushed = true;
        worldObject.onPickUp.AddListener(OnPickedUp);
        worldObject.onDrop.AddListener(OnDropped);
        
        // 强制确保有 Rigidbody 以便能够受到重力落下，防止飘在半空
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        // 更新显示
        if (fuelDisplay != null)
            fuelDisplayTransform = fuelDisplay.transform;
        UpdateFuelDisplay();
        enabled = worldObject.IsCarried;
    }

    private void OnDestroy()
    {
        if (worldObject == null)
            return;

        worldObject.onPickUp.RemoveListener(OnPickedUp);
        worldObject.onDrop.RemoveListener(OnDropped);
    }
    
    void Update()
    {
        if (!worldObject.IsCarried)
        {
            enabled = false;
            return;
        }

        // 当油桶被拿起时，检测右键加油
        if (Input.GetMouseButtonDown(1)) // 右键
        {
            TryRefillVehicle();
        }
        
        // 当油桶被拿起时，更新显示面向玩家（可选）
        if (fuelDisplayTransform != null && Time.time >= nextDisplayFacingUpdateTime)
        {
            nextDisplayFacingUpdateTime = Time.time + 0.05f;

            // 让文字面向主相机
            Camera mainCamera = GetCachedMainCamera();
            if (mainCamera != null)
            {
                Transform cameraTransform = cachedMainCameraTransform != null ? cachedMainCameraTransform : mainCamera.transform;
                Vector3 direction = fuelDisplayTransform.position - cameraTransform.position;
                if (direction.sqrMagnitude > 0.0001f)
                    fuelDisplayTransform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }
    }

    private void OnPickedUp(GameObject actor)
    {
        enabled = true;
    }

    private void OnDropped(GameObject actor)
    {
        enabled = false;
    }

    private Camera GetCachedMainCamera()
    {
        if (cachedMainCamera != null)
        {
            return cachedMainCamera;
        }

        if (Time.time < nextCameraLookupTime)
        {
            return null;
        }

        nextCameraLookupTime = Time.time + 0.5f;
        cachedMainCamera = Camera.main;
        cachedMainCameraTransform = cachedMainCamera != null ? cachedMainCamera.transform : null;
        return cachedMainCamera;
    }
    
    void TryRefillVehicle()
    {
        // 查找前方的车辆
        CarControl car = FindNearestVehicle();
        
        if (car == null)
        {
            LogRefillDebug("没有找到可加油的车辆");
            return;
        }
        
        // 检查距离
        float refillDistanceSq = refillDistance * refillDistance;
        float distanceSq = (transform.position - car.transform.position).sqrMagnitude;
        if (distanceSq > refillDistanceSq)
        {
            LogRefillTooFar(distanceSq);
            return;
        }
        
        // 获取当前车辆油量
        float currentCarFuel = car.GetCurrentFuel();
        
        if (currentCarFuel >= 100f)
        {
            LogRefillDebug("油箱已满，不需要加油");
            return;
        }
        
        if (fuelAmount <= 0f)
        {
            LogRefillDebug("油桶已空，无法加油");
            return;
        }
        
        // 计算需要加多少油才能满
        float neededFuel = 100f - currentCarFuel;
        // 实际能加的油量 = min(油桶剩余油量, 车辆所需油量)
        float fuelToTransfer = Mathf.Min(fuelAmount, neededFuel);
        
        // 加油
        car.AddFuel(fuelToTransfer);
        fuelAmount -= fuelToTransfer;
        
        // 更新显示
        UpdateFuelDisplay();
        
        // 加油反馈
        LogRefillDebug($"添加了 {fuelToTransfer:F1} 燃油，车辆油量: {car.GetCurrentFuel():F1}%，油桶剩余: {fuelAmount:F1}%");
        
        // 播放特效
        if (refillEffect != null)
        {
            GameObject effect = Instantiate(refillEffect, car.transform.position, Quaternion.identity);
            Destroy(effect, 1f);
        }
        
        // 如果油桶空了，可以选择自动丢弃或销毁
        if (fuelAmount <= 0f)
        {
            LogRefillDebug("油桶已空");
            // 可选：自动从手中丢弃
            // 可选：播放空桶音效
        }
    }

    private void LogRefillDebug(string message)
    {
        if (logRefillDebug)
        {
            Debug.Log(message);
        }
    }

    private void LogRefillTooFar(float distanceSq)
    {
        if (!logRefillDebug)
        {
            return;
        }

        float distance = Mathf.Sqrt(distanceSq);
        Debug.Log($"距离车辆太远 ({distance:F1}米)，需要靠近到 {refillDistance} 米以内");
    }
    
    CarControl FindNearestVehicle()
    {
        float refillDistanceSq = refillDistance * refillDistance;

        // 方法1：如果已经有缓存的车辆且在范围内，直接使用
        if (targetCar != null && (transform.position - targetCar.transform.position).sqrMagnitude <= refillDistanceSq)
        {
            return targetCar;
        }
        
        // 方法2：从短期缓存中查找车辆，避免连续加油尝试反复扫描整个场景
        CarControl[] cars = GetCachedCars();
        CarControl nearest = null;
        float minDistanceSq = refillDistanceSq;
        
        foreach (CarControl car in cars)
        {
            if (car == null) continue;

            float distanceSq = (transform.position - car.transform.position).sqrMagnitude;
            if (distanceSq < minDistanceSq)
            {
                minDistanceSq = distanceSq;
                nearest = car;
            }
        }
        
        targetCar = nearest;
        return nearest;
    }

    private static CarControl[] GetCachedCars()
    {
        if (s_cachedCars != null && Time.time < s_nextCarCacheRefreshTime)
        {
            return s_cachedCars;
        }

        s_nextCarCacheRefreshTime = Time.time + CarCacheRefreshInterval;
        s_cachedCars = FindObjectsOfType<CarControl>();
        return s_cachedCars;
    }
    
    void UpdateFuelDisplay()
    {
        if (fuelDisplay != null)
        {
            if (fuelDisplayTransform == null)
                fuelDisplayTransform = fuelDisplay.transform;

            int fuelPercent = Mathf.RoundToInt(fuelAmount);
            if (lastDisplayedFuelPercent != fuelPercent)
            {
                fuelDisplay.text = $"{fuelPercent}%";
                lastDisplayedFuelPercent = fuelPercent;
            }
            
            // 根据剩余油量改变颜色
            Color targetColor;
            if (fuelAmount <= 0)
            {
                targetColor = Color.red;
            }
            else if (fuelAmount < 30)
            {
                targetColor = Color.yellow;
            }
            else
            {
                targetColor = Color.green;
            }

            if (lastDisplayColor != targetColor || fuelDisplay.color != targetColor)
            {
                fuelDisplay.color = targetColor;
                lastDisplayColor = targetColor;
            }
        }
    }
    
    // 公共方法：设置油桶油量
    public void SetFuelAmount(float amount)
    {
        fuelAmount = Mathf.Clamp(amount, 0, maxFuelAmount);
        UpdateFuelDisplay();
    }
    
    // 公共方法：添加燃油到油桶
    public void AddFuel(float amount)
    {
        fuelAmount = Mathf.Min(maxFuelAmount, fuelAmount + amount);
        UpdateFuelDisplay();
    }
}
