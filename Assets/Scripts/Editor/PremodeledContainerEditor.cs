using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(PremodeledContainer))]
public class PremodeledContainerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 绘制基础公开皮肤面板（如 AllowedPrefabs, Skin Stages 列表）
        DrawDefaultInspector();

        PremodeledContainer container = (PremodeledContainer)target;

        GUILayout.Space(12);
        GUI.backgroundColor = new Color(0.15f, 0.6f, 1f); // 换个好看的蓝色按钮

        if (GUILayout.Button("⚡ 一键快速关联层级下的成品外观子物体", GUILayout.Height(30)))
        {
            Undo.RecordObject(container, "Auto Setup Skin Stages");

            if (container.skinStages == null)
                container.skinStages = new List<PremodeledContainer.ContainerSkinStage>();
            else
                container.skinStages.Clear();

            // 遍历并抓取当前盒子底下的子网格体成品状态
            foreach (Transform child in container.transform)
            {
                // 排除自带的物理外框 Cube，只抓取带有关卡状态的 Cabinet 变体
                if (child.name.ToLower().Contains("cube")) continue;

                int inferredCount = 0;
                
                // 智能从名字推断它包含了几个齿轮
                if (child.name.Contains("One") || child.name.Contains("1")) inferredCount = 1;
                else if (child.name.Contains("Two") || child.name.Contains("2")) inferredCount = 2;
                else if (child.name.Contains("Three") || child.name.Contains("3")) inferredCount = 3;
                else if (child.name.Contains("Four") || child.name.Contains("4")) inferredCount = 4;
                else if (child.name.Contains("Five") || child.name.Contains("5")) inferredCount = 5;

                PremodeledContainer.ContainerSkinStage newStage = new PremodeledContainer.ContainerSkinStage
                {
                    gearCount = inferredCount,
                    skinRootObject = child.gameObject
                };

                container.skinStages.Add(newStage);
            }

            // 按数量进行严谨排序
            container.skinStages.Sort((a, b) => a.gearCount.CompareTo(b.gearCount));

            EditorUtility.SetDirty(container);
            Debug.Log("<color=#40A0FF><b>[SkinTool]</b></color> 成品盒子视觉节点自动关联成功！");
        }
        GUI.backgroundColor = Color.white;
    }
}