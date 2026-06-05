using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System.IO;

public class KeyBindingSetupTool : EditorWindow {
    [MenuItem("Tools/Control UI Setup Tool")]
    public static void ShowWindow() {
        GetWindow<KeyBindingSetupTool>("Control UI Setup Tool");
    }

    private GameObject keyBindingPanel;
    private GameObject managerParent;

    void OnGUI() {
        GUILayout.Label("Key Binding UI Automatic Setup Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        keyBindingPanel = (GameObject)EditorGUILayout.ObjectField(
            "KeyBindingPanel GameObject", 
            keyBindingPanel, 
            typeof(GameObject), 
            true
        );

        managerParent = (GameObject)EditorGUILayout.ObjectField(
            "ManagerParent GameObject", 
            managerParent, 
            typeof(GameObject), 
            true
        );

        EditorGUILayout.Space();

        if (GUILayout.Button("Auto Setup & Configure Layout", GUILayout.Height(40))) {
            RunAutoSetup();
        }
    }

    private void RunAutoSetup() {
        if (keyBindingPanel == null) {
            // Try automatic matching by name
            keyBindingPanel = GameObject.Find("KeyBindingPanel");
            if (keyBindingPanel == null) {
                EditorUtility.DisplayDialog("Error", "Please assign the KeyBindingPanel GameObject from your scene hierarchy first.", "OK");
                return;
            }
        }

        if (managerParent == null) {
            managerParent = GameObject.Find("ManagerParent");
            if (managerParent == null) {
                managerParent = new GameObject("ManagerParent");
                Undo.RegisterCreatedObjectUndo(managerParent, "Create ManagerParent");
            }
        }

        // 1. Ensure InputManager is set up
        InputManager inputManager = managerParent.GetComponent<InputManager>();
        if (inputManager == null) {
            inputManager = managerParent.AddComponent<InputManager>();
            Undo.AddComponent<InputManager>(managerParent);
            Debug.Log("Attached InputManager script to ManagerParent GameObject.");
        }

        // Auto-initialize presets if empty in the inspector
        if (inputManager.PresetProfiles == null || inputManager.PresetProfiles.Count == 0) {
            inputManager.InitializePresetsContextMenu();
            Debug.Log("Auto-populated default keybindings inside InputManager.");
        }

        // 2. Attach KeyBindingUI to KeyBindingPanel
        KeyBindingUI uiScript = keyBindingPanel.GetComponent<KeyBindingUI>();
        if (uiScript == null) {
            uiScript = keyBindingPanel.AddComponent<KeyBindingUI>();
            Undo.AddComponent<KeyBindingUI>(keyBindingPanel);
            Debug.Log("Attached KeyBindingUI script to KeyBindingPanel GameObject.");
        }

        // Link keyBindingPanel reference to GameMenuManager if it exists
        GameMenuManager menuManager = FindObjectOfType<GameMenuManager>();
        if (menuManager != null) {
            // Use SerializedObject to access private serialize field
            SerializedObject serializedMenuManager = new SerializedObject(menuManager);
            SerializedProperty keyBindPanelProp = serializedMenuManager.FindProperty("keyBindingPanel");
            if (keyBindPanelProp != null) {
                keyBindPanelProp.objectReferenceValue = keyBindingPanel;
                serializedMenuManager.ApplyModifiedProperties();
                EditorUtility.SetDirty(menuManager);
                Debug.Log("Automatically assigned keyBindingPanel reference in GameMenuManager.");
            }
        }

        // 3. Search and reference Hierarchy components
        Transform root = keyBindingPanel.transform;

        // Top Bar
        Transform profileDropdownTrans = FindDeepChild(root, "ProfileDropdown");
        if (profileDropdownTrans != null) {
            uiScript.profileDropdown = profileDropdownTrans.GetComponent<TMP_Dropdown>();
        }

        // Content Scroll View
        Transform contentTrans = FindDeepChild(root, "Content");
        if (contentTrans != null) {
            uiScript.container = contentTrans;

            // Setup Layout Components on Content
            VerticalLayoutGroup vLayout = contentTrans.GetComponent<VerticalLayoutGroup>();
            if (vLayout == null) {
                vLayout = contentTrans.gameObject.AddComponent<VerticalLayoutGroup>();
            }
            vLayout.padding = new RectOffset(10, 10, 10, 10);
            vLayout.spacing = 8;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlHeight = true;
            vLayout.childControlWidth = true;
            vLayout.childForceExpandHeight = false;
            vLayout.childForceExpandWidth = true;

            ContentSizeFitter sizeFitter = contentTrans.GetComponent<ContentSizeFitter>();
            if (sizeFitter == null) {
                sizeFitter = contentTrans.gameObject.AddComponent<ContentSizeFitter>();
            }
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        // Bottom Bar / Status
        Transform statusTrans = FindDeepChild(root, "StatusTMP");
        if (statusTrans != null) {
            uiScript.statusText = statusTrans.GetComponent<TMP_Text>();
        }

        // File operations
        Transform fileNameTrans = FindDeepChild(root, "FileNameInputTMP");
        if (fileNameTrans != null) {
            uiScript.importExportFileNameInput = fileNameTrans.GetComponent<TMP_InputField>();
        }

        Transform exportTrans = FindDeepChild(root, "ButtonExport");
        if (exportTrans != null) {
            uiScript.exportButton = exportTrans.GetComponent<Button>();
        }

        Transform importTrans = FindDeepChild(root, "ButtonImport");
        if (importTrans != null) {
            uiScript.importButton = importTrans.GetComponent<Button>();
        }

        Transform resetTrans = FindDeepChild(root, "ButtonResetDefault");
        if (resetTrans != null) {
            uiScript.resetDefaultButton = resetTrans.GetComponent<Button>();
        }

        // 4. Create and Link Prefabs
        CreateDefaultPrefabs(uiScript);

        // Save modifications
        EditorUtility.SetDirty(uiScript);
        EditorUtility.SetDirty(keyBindingPanel);
        if (inputManager != null) {
            EditorUtility.SetDirty(inputManager);
        }

        EditorUtility.DisplayDialog("Success", "KeyBinding UI configured successfully! Referencing complete.", "Awesome");
    }

    private void CreateDefaultPrefabs(KeyBindingUI uiScript) {
        string folderPath = "Assets/Prefabs/UI";
        if (!System.IO.Directory.Exists(folderPath)) {
            System.IO.Directory.CreateDirectory(folderPath);
            AssetDatabase.Refresh();
        }

        string rowPath = folderPath + "/KeyBindRowPrefab.prefab";
        string headerPath = folderPath + "/KeyBindHeaderPrefab.prefab";

        // Generate Row Prefab if missing
        if (AssetDatabase.LoadAssetAtPath<GameObject>(rowPath) == null) {
            GameObject rowRoot = new GameObject("KeyBindRowPrefab", typeof(RectTransform));
            RectTransform rect = rowRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400, 45);

            HorizontalLayoutGroup hLayout = rowRoot.AddComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(10, 10, 5, 5);
            hLayout.spacing = 15;
            hLayout.childAlignment = TextAnchor.MiddleRight;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = false;

            // ActionName text child
            GameObject nameObj = new GameObject("ActionName", typeof(RectTransform));
            nameObj.transform.SetParent(rowRoot.transform);
            TMP_Text nameText = nameObj.AddComponent<TextMeshProUGUI>();
            nameText.text = "Action Name";
            nameText.fontSize = 16;
            nameText.alignment = TextAlignmentOptions.Left;
            
            LayoutElement layoutElement = nameObj.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1;

            // KeyButton child
            GameObject buttonObj = new GameObject("KeyButton", typeof(RectTransform));
            buttonObj.transform.SetParent(rowRoot.transform);
            RectTransform btnRect = buttonObj.GetComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(150, 35);

            buttonObj.AddComponent<CanvasRenderer>();
            Image btnImg = buttonObj.AddComponent<Image>();
            btnImg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            Button btn = buttonObj.AddComponent<Button>();

            // Button Key text
            GameObject keyTextObj = new GameObject("KeyText", typeof(RectTransform));
            keyTextObj.transform.SetParent(buttonObj.transform);
            TMP_Text keyText = keyTextObj.AddComponent<TextMeshProUGUI>();
            keyText.text = "Key";
            keyText.fontSize = 14;
            keyText.alignment = TextAlignmentOptions.Center;

            // Fit Text into Button
            RectTransform textRect = keyTextObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            // Save Prefab
            PrefabUtility.SaveAsPrefabAsset(rowRoot, rowPath);
            DestroyImmediate(rowRoot);
            Debug.Log($"Created default rowPrefab at: {rowPath}");
        }

        // Generate Header Prefab if missing
        if (AssetDatabase.LoadAssetAtPath<GameObject>(headerPath) == null) {
            GameObject headerRoot = new GameObject("KeyBindHeaderPrefab", typeof(RectTransform));
            RectTransform rect = headerRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400, 35);

            GameObject textObj = new GameObject("HeaderText", typeof(RectTransform));
            textObj.transform.SetParent(headerRoot.transform);
            TMP_Text headerText = textObj.AddComponent<TextMeshProUGUI>();
            headerText.text = "CATEGORY";
            headerText.fontSize = 18;
            headerText.fontStyle = FontStyles.Bold;
            headerText.alignment = TextAlignmentOptions.Left;
            headerText.color = new Color(0.3f, 0.7f, 1f, 1f);

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = new Vector2(-15, 0); // padding

            PrefabUtility.SaveAsPrefabAsset(headerRoot, headerPath);
            DestroyImmediate(headerRoot);
            Debug.Log($"Created default categoryHeaderPrefab at: {headerPath}");
        }

        // Link references to KeyBindingUI slots
        uiScript.rowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rowPath);
        uiScript.categoryHeaderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(headerPath);
    }

    private Transform FindDeepChild(Transform parent, string name) {
        foreach (Transform child in parent) {
            if (child.name == name) {
                return child;
            }
            Transform found = FindDeepChild(child, name);
            if (found != null) {
                return found;
            }
        }
        return null;
    }
}
