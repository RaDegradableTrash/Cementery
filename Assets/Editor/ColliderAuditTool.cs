using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditor.Experimental.SceneManagement;

public class ColliderAuditTool : EditorWindow
{
    class Issue { public GameObject gameObject; public MeshCollider meshCollider; public Rigidbody rb; public string locationPath; public string assetPath; }

    List<Issue> _issues = new List<Issue>();
    Vector2 _scroll;

    [MenuItem("Tools/Collider Audit")]
    public static void OpenWindow() => GetWindow<ColliderAuditTool>("Collider Audit");

    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Scan Project")) ScanProject();
        if (GUILayout.Button("Scan Open Scenes")) ScanOpenScenes();
        if (GUILayout.Button("Fix All (make convex)")) { if (EditorUtility.DisplayDialog("Fix All", "Set all offending MeshCollider.convex = true?","Yes","No")) FixAllMakeConvex(); }
        if (GUILayout.Button("Fix All (make kinematic)")) { if (EditorUtility.DisplayDialog("Fix All","Set all offending Rigidbody.isKinematic = true?","Yes","No")) FixAllMakeKinematic(); }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Issues found: {_issues.Count}", EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        for (int i = 0; i < _issues.Count; i++)
        {
            Issue it = _issues[i];
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(it.locationPath);
            EditorGUILayout.ObjectField("GameObject", it.gameObject, typeof(GameObject), false);
            EditorGUILayout.ObjectField("MeshCollider", it.meshCollider, typeof(MeshCollider), true);
            EditorGUILayout.ObjectField("Rigidbody", it.rb, typeof(Rigidbody), true);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ping")) EditorGUIUtility.PingObject(it.gameObject);
            if (GUILayout.Button("Select")) Selection.activeObject = it.gameObject;
            if (GUILayout.Button("Make Convex")) FixMakeConvex(it);
            if (GUILayout.Button("Make Kinematic")) FixMakeKinematic(it);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
    }

    void ScanProject()
    {
        _issues.Clear();
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            ScanGameObject(prefab, path, isPrefab:true);
        }

        // Also scan open scenes
        ScanOpenScenes();
        Repaint();
        Debug.Log($"ColliderAudit: Found {_issues.Count} issues.");
    }

    void ScanOpenScenes()
    {
        for (int si = 0; si < EditorSceneManager.sceneCount; si++)
        {
            Scene scene = EditorSceneManager.GetSceneAt(si);
            if (!scene.isLoaded) continue;
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (var root in roots)
                ScanGameObject(root, scene.path, isPrefab:false);
        }
        Repaint();
    }

    void ScanGameObject(GameObject go, string contextPath, bool isPrefab)
    {
        MeshCollider[] mcs = go.GetComponentsInChildren<MeshCollider>(true);
        foreach (var mc in mcs)
        {
            if (mc == null) continue;
            if (mc.convex) continue;

            // find Rigidbody on same or parent
            Rigidbody rb = mc.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                Issue it = new Issue();
                it.gameObject = mc.gameObject;
                it.meshCollider = mc;
                it.rb = rb;
                it.locationPath = BuildHierarchyPath(mc.gameObject);
                it.assetPath = contextPath;
                _issues.Add(it);
            }
        }
    }

    string BuildHierarchyPath(GameObject go)
    {
        string s = go.name;
        Transform t = go.transform.parent;
        while (t != null)
        {
            s = t.name + "/" + s;
            t = t.parent;
        }
        return s;
    }

    void FixMakeConvex(Issue it)
    {
        if (it == null || it.meshCollider == null) return;
        Undo.RecordObject(it.meshCollider, "Set MeshCollider.convex");
        it.meshCollider.convex = true;
        EditorUtility.SetDirty(it.meshCollider);

        if (!string.IsNullOrEmpty(it.assetPath) && AssetDatabase.IsMainAssetAtPathLoaded(it.assetPath))
            PrefabUtility.SavePrefabAsset(it.meshCollider.gameObject.transform.root.gameObject);
        else
            MarkSceneDirtyForObject(it.meshCollider.gameObject);

        _issues.Remove(it);
        Repaint();
    }

    void FixMakeKinematic(Issue it)
    {
        if (it == null || it.rb == null) return;
        Undo.RecordObject(it.rb, "Set Rigidbody.isKinematic");
        it.rb.isKinematic = true;
        EditorUtility.SetDirty(it.rb);
        MarkSceneDirtyForObject(it.rb.gameObject);
        _issues.Remove(it);
        Repaint();
    }

    void FixAllMakeConvex()
    {
        // copy list to avoid modification during iteration
        var copy = new List<Issue>(_issues);
        foreach (var it in copy) FixMakeConvex(it);
        Debug.Log("ColliderAudit: Fixed all (convex=true)");
    }

    void FixAllMakeKinematic()
    {
        var copy = new List<Issue>(_issues);
        foreach (var it in copy) FixMakeKinematic(it);
        Debug.Log("ColliderAudit: Fixed all (rb.isKinematic=true)");
    }

    void MarkSceneDirtyForObject(GameObject obj)
    {
        if (obj == null) return;
        Scene s = obj.scene;
        if (s.IsValid()) EditorSceneManager.MarkSceneDirty(s);
    }
}
