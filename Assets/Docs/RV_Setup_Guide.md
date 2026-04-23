# 房车系统配置手册 (RV System Setup Guide)

本文档将指导你如何在 Unity 编辑器中配置已生成的房车脚本。

## 1. 基础物理层级搭建 (Hierarchy Setup)
在 Hierarchy 中创建以下结构（以 `RV_Root` 为核心）：

*   **RV_Root** (GameObject) ← 挂载 `Rigidbody`, `RVController`, `RVStateMachine`, `RVCameraController`
    *   **Colliders** (GameObject) ← 放置车身的 `BoxCollider` (建议分块放置以贴合形状)
    *   **WheelColliders** (GameObject)
        *   **Wheel_FL** (WheelCollider) ← 前左转向轮
        *   **Wheel_FR** (WheelCollider) ← 前右转向轮
        *   **Wheel_RL** (WheelCollider) ← 后左驱动轮
        *   **Wheel_RR** (WheelCollider) ← 后右驱动轮
    *   **Interior** (GameObject)
        *   **InteriorTrigger** (GameObject) ← 挂载 `RVInteriorInteraction`, 需包含 `BoxCollider` (Is Trigger)
        *   **InteriorCamera** (Camera) ← 驾驶位相机
    *   **ExteriorCamera** (Cinemachine FreeLook 或 Camera) ← 车外跟随相机
    *   **CoM_Pivot** (Empty GameObject) ← 放置在车辆底盘中心偏下的位置

---

## 2. 组件参数配置 (Component Settings)

### 2.1 Rigidbody (刚体)
*   **Mass**: 4500 (房车通常较重)
*   **Drag**: 0.05
*   **Angular Drag**: 0.1

### 2.2 RVController
*   **Drive Wheels**: 拖入 `Wheel_RL`, `Wheel_RR`
*   **Steer Wheels**: 拖入 `Wheel_FL`, `Wheel_FR`
*   **All Wheels**: 拖入全部 4 个 WheelCollider
*   **Center Of Mass**: 拖入 `CoM_Pivot`
*   **Motor Torque**: 2500 - 3500
*   **Brake Torque**: 5000

### 2.3 AntiRollBar (防倾斜 - 关键)
*建议前后轴各挂一个或直接在根节点挂两个：*
*   **Anti Roll Force**: 5000 (如果转弯易翻车，请调高此值)
*   **Wheel L/R**: 对应左右轮对。

### 2.4 RVInteriorInteraction (车内交互)
*   **Player Parent**: 拖入 `RV_Root` (确保玩家进入后随车移动)
*   **Player Tag**: 确保你的玩家 Prefab Tag 为 `Player`

---

## 3. 常见问题调试 (Troubleshooting)

1.  **车身像弹簧一样跳动？**
    *   检查 `WheelCollider` 的 `Suspension Distance` (建议 0.2-0.3)
    *   提高 `Suspension Spring` 到 35000，`Damper` 到 4500。
2.  **转弯半径太大？**
    *   在 `RVController` 中增加 `Max Steer Angle` (建议 30-40)。
3.  **玩家在车内移动抖动？**
    *   确保玩家的 `CharacterController` 在 `FixedUpdate` 中进行物理计算。
    *   如果依然抖动，尝试在 `RVInteriorInteraction` 中通过脚本手动同步 `transform.position += rv_velocity * Time.fixedDeltaTime`。

---

## 4. 交互测试流程
1.  **进入车辆**：玩家走入 `InteriorTrigger`，观察 Hierarchy 中 Player 是否成为了 `RV_Root` 的子物体。
2.  **启动驾驶**：在 `RVStateMachine` 中手动勾选 `Current State` 为 `Active`（或编写脚本在玩家坐上驾驶位时触发此切换）。
3.  **视角切换**：运行模式下按 `C` 键切换内外相机。
