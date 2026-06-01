using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(PremodeledContainer))]
public class PremodeledContainerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PremodeledContainer container = (PremodeledContainer)target;

        GUILayout.Space(12);
        GUI.backgroundColor = new Color(0.1f, 0.75f, 0.5f); // 醒目的绿色按钮

        if (GUILayout.Button("⚡ 一键秒级抓取当前盒子内部的 GearSpots", GUILayout.Height(35)))
        {
            Undo.RecordObject(container, "Grab Current Container Slots");

            if (container.placementSlots == null)
                container.placementSlots = new List<Transform>();
            else
                container.placementSlots.Clear();

            FindSpotsRecursively(container.transform, container.placementSlots);

            // 按名字自然字典序排序
            container.placementSlots.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

            EditorUtility.SetDirty(container);
            Debug.Log($"<color=#10FF80><b>[ContainerTool]</b></color> 成功！已为当前盒子关联了 <b>{container.placementSlots.Count}</b> 个放置点。");
        }
        GUI.backgroundColor = Color.white;
    }

    private void FindSpotsRecursively(Transform current, List<Transform> resultList)
    {
        foreach (Transform child in current)
        {
            if (child.name.ToLower().Contains("spot"))
            {
                resultList.Add(child);
            }
            if (child.childCount > 0)
            {
                FindSpotsRecursively(child, resultList);
            }
        }
    }
}