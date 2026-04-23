# Cementery prealpha v0.0.2

## 更新亮点
- 完成背包放置与存储基础流程（BackStorage placement / PackStorage basics）。
- 改进 carry 交互：滚轮调节距离、距离限制提示 UI、自适应射线距离逻辑。
- 调整 carry 物理手感：移除携带摩擦导致的卡顿，并优化跟随稳定性。

## 详细变更
- `PackStorage Basics Done`
- `BackStorage placement`
- `Carry: scroll distance and limit UI feedback`
- `feat: removed carry friction, added adaptive raycast`
- `Refine carry with adaptive ray-distance behavior`

## 已知问题
- 仍处于 prealpha 阶段，可能存在输入边缘情况与少量交互稳定性问题。
- 不同机器/帧率下的物理与摄像机体感可能仍有差异，后续会继续调优。

## 兼容与说明
- 推荐使用 Unity 2022.3 LTS 版本打开项目。
- 本版本以功能验证为主，面向测试与反馈，不建议直接作为正式发布版本。
