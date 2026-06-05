using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System.IO;
using UnityEditor.Events;
using UnityEngine.Events;

public class RecreateUIHierarchyTool : EditorWindow {
    [MenuItem("Tools/Recreate KeyBinding UI Panel")]
    public static void RecreateUI() {
        // 1. Find or create Canvas in the active scene
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) {
            GameObject canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");
        }

        // 2. Find or create PauseMenuManager
        GameMenuManager menuManager = FindObjectOfType<GameMenuManager>();
        if (menuManager == null) {
            GameObject managerGo = GameObject.Find("PauseMenuManager");
            if (managerGo == null) {
                managerGo = new GameObject("PauseMenuManager");
            }
            menuManager = managerGo.AddComponent<GameMenuManager>();
            Undo.RegisterCreatedObjectUndo(managerGo, "Create PauseMenuManager");
        }

        // 3. Clean up any existing PauseMenuContainer or legacy direct-child panels under Canvas to avoid duplicates
        Transform containerTrans = canvas.transform.Find("PauseMenuContainer");
        if (containerTrans != null) {
            if (EditorUtility.DisplayDialog("Confirm Recreate", "PauseMenuContainer already exists. Recreating will delete it and all child panels. Continue?", "Yes", "No")) {
                Undo.DestroyObjectImmediate(containerTrans.gameObject);
            } else {
                return;
            }
        }

        // Also clean up any loose legacy panels directly under Canvas if they exist
        string[] legacyPanelNames = { "KeyBindingPanel", "MainPausePanel", "MultiplayerPanel" };
        foreach (string pName in legacyPanelNames) {
            Transform legacyPanel = canvas.transform.Find(pName);
            if (legacyPanel != null) {
                Undo.DestroyObjectImmediate(legacyPanel.gameObject);
            }
        }

        GameObject containerGo = new GameObject("PauseMenuContainer", typeof(RectTransform));
        containerGo.transform.SetParent(canvas.transform, false);
        RectTransform containerRect = containerGo.GetComponent<RectTransform>();
        containerRect.anchorMin = Vector2.zero;
        containerRect.anchorMax = Vector2.one;
        containerRect.sizeDelta = Vector2.zero;
        Undo.RegisterCreatedObjectUndo(containerGo, "Create PauseMenuContainer");

        // Styling Color Constants
        Color panelBgColor = new Color(0.08f, 0.08f, 0.08f, 0.98f); // Flat deep black-gray
        Color buttonBgColor = Color.white;
        Color buttonTextColor = Color.black;
        Color labelTextColor = Color.white;

        // ====================================================================
        // A. MAIN PAUSE PANEL
        // ====================================================================
        GameObject mainPanel = new GameObject("MainPausePanel", typeof(RectTransform));
        mainPanel.transform.SetParent(containerGo.transform, false);
        RectTransform mainRect = mainPanel.GetComponent<RectTransform>();
        mainRect.anchorMin = new Vector2(0.5f, 0.5f);
        mainRect.anchorMax = new Vector2(0.5f, 0.5f);
        mainRect.anchoredPosition = Vector2.zero;
        mainRect.sizeDelta = new Vector2(700, 520);

        mainPanel.AddComponent<CanvasRenderer>();
        Image mainImg = mainPanel.AddComponent<Image>();
        mainImg.color = panelBgColor;

        // Title
        GameObject mainTitle = new GameObject("TitleTMP", typeof(RectTransform));
        mainTitle.transform.SetParent(mainPanel.transform, false);
        RectTransform mainTitleRect = mainTitle.GetComponent<RectTransform>();
        mainTitleRect.anchorMin = new Vector2(0.5f, 1f);
        mainTitleRect.anchorMax = new Vector2(0.5f, 1f);
        mainTitleRect.pivot = new Vector2(0.5f, 1f);
        mainTitleRect.anchoredPosition = new Vector2(0, -35);
        mainTitleRect.sizeDelta = new Vector2(500, 50);
        TMP_Text mainTitleText = mainTitle.AddComponent<TextMeshProUGUI>();
        mainTitleText.text = "PAUSE MENU";
        mainTitleText.fontSize = 32;
        mainTitleText.fontStyle = FontStyles.Bold;
        mainTitleText.color = labelTextColor;
        mainTitleText.alignment = TextAlignmentOptions.Center;

        // Button Grid Container
        GameObject gridObj = new GameObject("ButtonGrid", typeof(RectTransform));
        gridObj.transform.SetParent(mainPanel.transform, false);
        RectTransform gridRect = gridObj.GetComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0.5f, 0.5f);
        gridRect.anchorMax = new Vector2(0.5f, 0.5f);
        gridRect.pivot = new Vector2(0.5f, 0.5f);
        gridRect.anchoredPosition = new Vector2(0, 10);
        gridRect.sizeDelta = new Vector2(620, 240);

        GridLayoutGroup grid = gridObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(290, 45);
        grid.spacing = new Vector2(20, 15);
        grid.childAlignment = TextAnchor.MiddleCenter;

        // Create 6 functional / placeholder sub-menu buttons
        GameObject btnControls = CreateFlatButton("ButtonControls", gridObj.transform, buttonBgColor, buttonTextColor, "CONTROLS & KEYBINDS");
        GameObject btnMultiplayer = CreateFlatButton("ButtonMultiplayer", gridObj.transform, buttonBgColor, buttonTextColor, "MULTIPLAYER LOBBY");
        GameObject btnPlaceholder1 = CreateFlatButton("ButtonPlaceholder1", gridObj.transform, buttonBgColor, buttonTextColor, "VIDEO SETTINGS");
        GameObject btnPlaceholder2 = CreateFlatButton("ButtonPlaceholder2", gridObj.transform, buttonBgColor, buttonTextColor, "AUDIO SETTINGS");
        GameObject btnPlaceholder3 = CreateFlatButton("ButtonPlaceholder3", gridObj.transform, buttonBgColor, buttonTextColor, "GAMEPLAY OPTIONS");
        GameObject btnPlaceholder4 = CreateFlatButton("ButtonPlaceholder4", gridObj.transform, buttonBgColor, buttonTextColor, "CREDITS & ABOUT");

        // Bottom Action Row (Resume & Quit)
        GameObject bottomRow = new GameObject("BottomActionRow", typeof(RectTransform));
        bottomRow.transform.SetParent(mainPanel.transform, false);
        RectTransform bottomRowRect = bottomRow.GetComponent<RectTransform>();
        bottomRowRect.anchorMin = new Vector2(0.5f, 0f);
        bottomRowRect.anchorMax = new Vector2(0.5f, 0f);
        bottomRowRect.pivot = new Vector2(0.5f, 0f);
        bottomRowRect.anchoredPosition = new Vector2(0, 40);
        bottomRowRect.sizeDelta = new Vector2(600, 45);

        HorizontalLayoutGroup hLayout = bottomRow.AddComponent<HorizontalLayoutGroup>();
        hLayout.spacing = 20;
        hLayout.childAlignment = TextAnchor.MiddleCenter;
        hLayout.childControlWidth = true;
        hLayout.childControlHeight = true;

        GameObject btnResume = CreateFlatButton("ButtonResume", bottomRow.transform, buttonBgColor, buttonTextColor, "RESUME GAME");
        GameObject btnQuit = CreateFlatButton("ButtonQuit", bottomRow.transform, buttonBgColor, buttonTextColor, "QUIT TO DESKTOP");

        // ====================================================================
        // B. KEYBINDING PANEL (Restored original style)
        // ====================================================================
        GameObject kbPanel = new GameObject("KeyBindingPanel", typeof(RectTransform));
        kbPanel.transform.SetParent(containerGo.transform, false);
        RectTransform kbRect = kbPanel.GetComponent<RectTransform>();
        kbRect.anchorMin = new Vector2(0.5f, 0.5f);
        kbRect.anchorMax = new Vector2(0.5f, 0.5f);
        kbRect.anchoredPosition = Vector2.zero;
        kbRect.sizeDelta = new Vector2(700, 520);

        kbPanel.AddComponent<CanvasRenderer>();
        Image kbImg = kbPanel.AddComponent<Image>();
        kbImg.color = panelBgColor;

        KeyBindingUI uiScript = kbPanel.AddComponent<KeyBindingUI>();

        // Top bar
        GameObject kbTopBar = new GameObject("TopBarEmpty", typeof(RectTransform));
        kbTopBar.transform.SetParent(kbPanel.transform, false);
        RectTransform kbTopBarRect = kbTopBar.GetComponent<RectTransform>();
        kbTopBarRect.anchorMin = new Vector2(0, 1);
        kbTopBarRect.anchorMax = new Vector2(1, 1);
        kbTopBarRect.pivot = new Vector2(0.5f, 1);
        kbTopBarRect.anchoredPosition = new Vector2(0, -10);
        kbTopBarRect.sizeDelta = new Vector2(-40, 50);

        GameObject kbTitle = new GameObject("TitleTMP", typeof(RectTransform));
        kbTitle.transform.SetParent(kbTopBar.transform, false);
        RectTransform kbTitleRect = kbTitle.GetComponent<RectTransform>();
        kbTitleRect.anchorMin = new Vector2(0, 0.5f);
        kbTitleRect.anchorMax = new Vector2(0, 0.5f);
        kbTitleRect.pivot = new Vector2(0, 0.5f);
        kbTitleRect.anchoredPosition = new Vector2(10, 0);
        kbTitleRect.sizeDelta = new Vector2(250, 40);
        TMP_Text kbTitleText = kbTitle.AddComponent<TextMeshProUGUI>();
        kbTitleText.text = "CONTROLS";
        kbTitleText.fontSize = 28;
        kbTitleText.fontStyle = FontStyles.Bold;
        kbTitleText.color = labelTextColor;

        // Profile Dropdown
        GameObject dropdownObj = new GameObject("ProfileDropdown", typeof(RectTransform));
        dropdownObj.transform.SetParent(kbTopBar.transform, false);
        RectTransform dropdownRect = dropdownObj.GetComponent<RectTransform>();
        dropdownRect.anchorMin = new Vector2(1, 0.5f);
        dropdownRect.anchorMax = new Vector2(1, 0.5f);
        dropdownRect.pivot = new Vector2(1, 0.5f);
        dropdownRect.anchoredPosition = new Vector2(-10, 0);
        dropdownRect.sizeDelta = new Vector2(160, 32);

        dropdownObj.AddComponent<CanvasRenderer>();
        Image ddImg = dropdownObj.AddComponent<Image>();
        ddImg.color = buttonBgColor;
        TMP_Dropdown dropdown = dropdownObj.AddComponent<TMP_Dropdown>();
        uiScript.profileDropdown = dropdown;

        GameObject ddLabelObj = new GameObject("Label", typeof(RectTransform));
        ddLabelObj.transform.SetParent(dropdownObj.transform, false);
        RectTransform ddLabelRect = ddLabelObj.GetComponent<RectTransform>();
        ddLabelRect.anchorMin = Vector2.zero;
        ddLabelRect.anchorMax = Vector2.one;
        ddLabelRect.sizeDelta = new Vector2(-30, 0);
        TMP_Text ddLabelText = ddLabelObj.AddComponent<TextMeshProUGUI>();
        ddLabelText.text = "Default";
        ddLabelText.fontSize = 14;
        ddLabelText.color = buttonTextColor;
        ddLabelText.alignment = TextAlignmentOptions.Left;
        dropdown.captionText = ddLabelText;

        GameObject templateObj = new GameObject("Template", typeof(RectTransform));
        templateObj.transform.SetParent(dropdownObj.transform, false);
        templateObj.SetActive(false);
        RectTransform tempRect = templateObj.GetComponent<RectTransform>();
        tempRect.anchorMin = new Vector2(0, 0);
        tempRect.anchorMax = new Vector2(1, 0);
        tempRect.pivot = new Vector2(0.5f, 1);
        tempRect.sizeDelta = new Vector2(0, 150);
        templateObj.AddComponent<ScrollRect>();
        Image tempImg = templateObj.AddComponent<Image>();
        tempImg.color = buttonBgColor;
        dropdown.template = tempRect;

        // Center Scroll
        GameObject centerScroll = new GameObject("CenterScrollEmpty", typeof(RectTransform));
        centerScroll.transform.SetParent(kbPanel.transform, false);
        RectTransform scrollRect = centerScroll.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0, 0);
        scrollRect.anchorMax = new Vector2(1, 1);
        scrollRect.offsetMin = new Vector2(20, 120);
        scrollRect.offsetMax = new Vector2(-20, -70);

        ScrollRect scrollComponent = centerScroll.AddComponent<ScrollRect>();
        scrollComponent.horizontal = false;
        scrollComponent.vertical = true;

        GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform));
        viewportObj.transform.SetParent(centerScroll.transform, false);
        RectTransform viewRect = viewportObj.GetComponent<RectTransform>();
        viewRect.anchorMin = Vector2.zero;
        viewRect.anchorMax = Vector2.one;
        viewRect.sizeDelta = Vector2.zero;
        viewportObj.AddComponent<RectMask2D>();
        scrollComponent.viewport = viewRect;

        GameObject contentObj = new GameObject("Content", typeof(RectTransform));
        contentObj.transform.SetParent(viewportObj.transform, false);
        RectTransform contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 0);

        VerticalLayoutGroup vLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        vLayout.padding = new RectOffset(10, 15, 10, 10);
        vLayout.spacing = 6;
        vLayout.childAlignment = TextAnchor.UpperCenter;
        vLayout.childControlHeight = true;
        vLayout.childControlWidth = true;
        vLayout.childForceExpandHeight = false;
        vLayout.childForceExpandWidth = true;

        ContentSizeFitter sizeFitter = contentObj.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollComponent.content = contentRect;
        uiScript.container = contentRect.transform;

        // Bottom Bar
        GameObject kbBottomBar = new GameObject("BottomBarEmpty", typeof(RectTransform));
        kbBottomBar.transform.SetParent(kbPanel.transform, false);
        RectTransform kbBottomBarRect = kbBottomBar.GetComponent<RectTransform>();
        kbBottomBarRect.anchorMin = new Vector2(0, 0);
        kbBottomBarRect.anchorMax = new Vector2(1, 0);
        kbBottomBarRect.pivot = new Vector2(0.5f, 0);
        kbBottomBarRect.anchoredPosition = new Vector2(0, 10);
        kbBottomBarRect.sizeDelta = new Vector2(-40, 100);

        // Status Text
        GameObject statusObj = new GameObject("StatusTMP", typeof(RectTransform));
        statusObj.transform.SetParent(kbBottomBar.transform, false);
        RectTransform statusRect = statusObj.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0, 1);
        statusRect.anchorMax = new Vector2(1, 1);
        statusRect.pivot = new Vector2(0.5f, 1);
        statusRect.anchoredPosition = new Vector2(0, 0);
        statusRect.sizeDelta = new Vector2(0, 25);
        TMP_Text statusText = statusObj.AddComponent<TextMeshProUGUI>();
        statusText.text = "Ready.";
        statusText.fontSize = 14;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = labelTextColor;
        uiScript.statusText = statusText;

        // File Operations
        GameObject fileOps = new GameObject("FileOperations", typeof(RectTransform));
        fileOps.transform.SetParent(kbBottomBar.transform, false);
        RectTransform fileOpsRect = fileOps.GetComponent<RectTransform>();
        fileOpsRect.anchorMin = new Vector2(0, 0);
        fileOpsRect.anchorMax = new Vector2(0.55f, 0);
        fileOpsRect.pivot = new Vector2(0, 0);
        fileOpsRect.anchoredPosition = new Vector2(10, 10);
        fileOpsRect.sizeDelta = new Vector2(0, 45);

        GameObject inputObj = new GameObject("FileNameInputTMP", typeof(RectTransform));
        inputObj.transform.SetParent(fileOps.transform, false);
        RectTransform inputRect = inputObj.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0, 0.5f);
        inputRect.anchorMax = new Vector2(0.45f, 0.5f);
        inputRect.pivot = new Vector2(0, 0.5f);
        inputRect.anchoredPosition = new Vector2(0, 0);
        inputRect.sizeDelta = new Vector2(0, 32);

        inputObj.AddComponent<CanvasRenderer>();
        Image inputImg = inputObj.AddComponent<Image>();
        inputImg.color = Color.white;
        TMP_InputField inputField = inputObj.AddComponent<TMP_InputField>();
        uiScript.importExportFileNameInput = inputField;

        GameObject textArea = new GameObject("TextArea", typeof(RectTransform));
        textArea.transform.SetParent(inputObj.transform, false);
        RectTransform taRect = textArea.GetComponent<RectTransform>();
        taRect.anchorMin = Vector2.zero;
        taRect.anchorMax = Vector2.one;
        taRect.sizeDelta = new Vector2(-10, 0);
        textArea.AddComponent<RectMask2D>();

        GameObject inputTextObj = new GameObject("Text", typeof(RectTransform));
        inputTextObj.transform.SetParent(textArea.transform, false);
        RectTransform itRect = inputTextObj.GetComponent<RectTransform>();
        itRect.anchorMin = Vector2.zero;
        itRect.anchorMax = Vector2.one;
        itRect.sizeDelta = Vector2.zero;
        TMP_Text inputText = inputTextObj.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 13;
        inputText.color = Color.black;
        inputText.alignment = TextAlignmentOptions.Left;
        inputField.textComponent = inputText;

        GameObject placeholderObj = new GameObject("Placeholder", typeof(RectTransform));
        placeholderObj.transform.SetParent(textArea.transform, false);
        RectTransform phRect = placeholderObj.GetComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.sizeDelta = Vector2.zero;
        TMP_Text phText = placeholderObj.AddComponent<TextMeshProUGUI>();
        phText.text = "Enter profile name...";
        phText.fontSize = 13;
        phText.fontStyle = FontStyles.Italic;
        phText.color = Color.gray;
        phText.alignment = TextAlignmentOptions.Left;
        inputField.placeholder = phText;

        GameObject exportBtnObj = CreateFlatButton("ButtonExport", fileOps.transform, buttonBgColor, buttonTextColor, "EXPORT");
        RectTransform exportBtnRect = exportBtnObj.GetComponent<RectTransform>();
        exportBtnRect.anchorMin = new Vector2(0.5f, 0.5f);
        exportBtnRect.anchorMax = new Vector2(0.72f, 0.5f);
        exportBtnRect.pivot = new Vector2(0, 0.5f);
        exportBtnRect.anchoredPosition = new Vector2(5, 0);
        exportBtnRect.sizeDelta = new Vector2(0, 32);
        uiScript.exportButton = exportBtnObj.GetComponent<Button>();

        GameObject importBtnObj = CreateFlatButton("ButtonImport", fileOps.transform, buttonBgColor, buttonTextColor, "IMPORT");
        RectTransform importBtnRect = importBtnObj.GetComponent<RectTransform>();
        importBtnRect.anchorMin = new Vector2(0.75f, 0.5f);
        importBtnRect.anchorMax = new Vector2(0.97f, 0.5f);
        importBtnRect.pivot = new Vector2(0, 0.5f);
        importBtnRect.anchoredPosition = new Vector2(5, 0);
        importBtnRect.sizeDelta = new Vector2(0, 32);
        uiScript.importButton = importBtnObj.GetComponent<Button>();

        // Controls Back and Reset buttons
        GameObject btnKbReset = CreateFlatButton("ButtonResetDefault", kbBottomBar.transform, buttonBgColor, buttonTextColor, "RESET DEFAULT");
        RectTransform resetBtnRect = btnKbReset.GetComponent<RectTransform>();
        resetBtnRect.anchorMin = new Vector2(1f, 0);
        resetBtnRect.anchorMax = new Vector2(1f, 0);
        resetBtnRect.pivot = new Vector2(1f, 0);
        resetBtnRect.anchoredPosition = new Vector2(-125, 10);
        resetBtnRect.sizeDelta = new Vector2(110, 32);
        uiScript.resetDefaultButton = btnKbReset.GetComponent<Button>();

        GameObject btnKbBack = CreateFlatButton("ButtonBack", kbBottomBar.transform, buttonBgColor, buttonTextColor, "BACK");
        RectTransform kbBackRect = btnKbBack.GetComponent<RectTransform>();
        kbBackRect.anchorMin = new Vector2(1f, 0);
        kbBackRect.anchorMax = new Vector2(1f, 0);
        kbBackRect.pivot = new Vector2(1f, 0);
        kbBackRect.anchoredPosition = new Vector2(-10, 10);
        kbBackRect.sizeDelta = new Vector2(100, 32);

        // ====================================================================
        // C. MULTIPLAYER PANEL
        // ====================================================================
        GameObject mpPanel = new GameObject("MultiplayerPanel", typeof(RectTransform));
        mpPanel.transform.SetParent(containerGo.transform, false);
        RectTransform mpRect = mpPanel.GetComponent<RectTransform>();
        mpRect.anchorMin = new Vector2(0.5f, 0.5f);
        mpRect.anchorMax = new Vector2(0.5f, 0.5f);
        mpRect.anchoredPosition = Vector2.zero;
        mpRect.sizeDelta = new Vector2(700, 520);

        mpPanel.AddComponent<CanvasRenderer>();
        Image mpImg = mpPanel.AddComponent<Image>();
        mpImg.color = panelBgColor;

        // Title
        GameObject mpTitle = new GameObject("TitleTMP", typeof(RectTransform));
        mpTitle.transform.SetParent(mpPanel.transform, false);
        RectTransform mpTitleRect = mpTitle.GetComponent<RectTransform>();
        mpTitleRect.anchorMin = new Vector2(0.5f, 1f);
        mpTitleRect.anchorMax = new Vector2(0.5f, 1f);
        mpTitleRect.pivot = new Vector2(0.5f, 1f);
        mpTitleRect.anchoredPosition = new Vector2(0, -35);
        mpTitleRect.sizeDelta = new Vector2(500, 40);
        TMP_Text mpTitleText = mpTitle.AddComponent<TextMeshProUGUI>();
        mpTitleText.text = "MULTIPLAYER LOBBY";
        mpTitleText.fontSize = 28;
        mpTitleText.fontStyle = FontStyles.Bold;
        mpTitleText.color = labelTextColor;
        mpTitleText.alignment = TextAlignmentOptions.Center;

        // Container for Input & Buttons
        GameObject mpContent = new GameObject("ContentGroup", typeof(RectTransform));
        mpContent.transform.SetParent(mpPanel.transform, false);
        RectTransform mpContentRect = mpContent.GetComponent<RectTransform>();
        mpContentRect.anchorMin = new Vector2(0.5f, 0.5f);
        mpContentRect.anchorMax = new Vector2(0.5f, 0.5f);
        mpContentRect.pivot = new Vector2(0.5f, 0.5f);
        mpContentRect.anchoredPosition = new Vector2(0, 10);
        mpContentRect.sizeDelta = new Vector2(360, 280);

        VerticalLayoutGroup mpVLayout = mpContent.AddComponent<VerticalLayoutGroup>();
        mpVLayout.spacing = 15;
        mpVLayout.childAlignment = TextAnchor.MiddleCenter;
        mpVLayout.childControlWidth = true;
        mpVLayout.childControlHeight = false;

        // Code Input Field
        GameObject mpInputObj = new GameObject("JoinCodeInputField", typeof(RectTransform));
        mpInputObj.transform.SetParent(mpContent.transform, false);
        RectTransform mpInputRect = mpInputObj.GetComponent<RectTransform>();
        mpInputRect.sizeDelta = new Vector2(320, 38);

        mpInputObj.AddComponent<CanvasRenderer>();
        Image mpInputImg = mpInputObj.AddComponent<Image>();
        mpInputImg.color = Color.white;
        TMP_InputField mpInputField = mpInputObj.AddComponent<TMP_InputField>();

        GameObject mpTextArea = new GameObject("TextArea", typeof(RectTransform));
        mpTextArea.transform.SetParent(mpInputObj.transform, false);
        RectTransform mpTaRect = mpTextArea.GetComponent<RectTransform>();
        mpTaRect.anchorMin = Vector2.zero;
        mpTaRect.anchorMax = Vector2.one;
        mpTaRect.sizeDelta = new Vector2(-10, 0);
        mpTextArea.AddComponent<RectMask2D>();

        GameObject mpInputTextObj = new GameObject("Text", typeof(RectTransform));
        mpInputTextObj.transform.SetParent(mpTextArea.transform, false);
        RectTransform mpItRect = mpInputTextObj.GetComponent<RectTransform>();
        mpItRect.anchorMin = Vector2.zero;
        mpItRect.anchorMax = Vector2.one;
        mpItRect.sizeDelta = Vector2.zero;
        TMP_Text mpInputText = mpInputTextObj.AddComponent<TextMeshProUGUI>();
        mpInputText.fontSize = 14;
        mpInputText.color = Color.black;
        mpInputText.alignment = TextAlignmentOptions.Left;
        mpInputField.textComponent = mpInputText;

        GameObject mpPlaceholderObj = new GameObject("Placeholder", typeof(RectTransform));
        mpPlaceholderObj.transform.SetParent(mpTextArea.transform, false);
        RectTransform mpPhRect = mpPlaceholderObj.GetComponent<RectTransform>();
        mpPhRect.anchorMin = Vector2.zero;
        mpPhRect.anchorMax = Vector2.one;
        mpPhRect.sizeDelta = Vector2.zero;
        TMP_Text mpPhText = mpPlaceholderObj.AddComponent<TextMeshProUGUI>();
        mpPhText.text = "Enter Room/Join Code...";
        mpPhText.fontSize = 14;
        mpPhText.fontStyle = FontStyles.Italic;
        mpPhText.color = Color.gray;
        mpPhText.alignment = TextAlignmentOptions.Left;
        mpInputField.placeholder = mpPhText;

        // Buttons Row
        GameObject mpBtnRow = new GameObject("ButtonsRow", typeof(RectTransform));
        mpBtnRow.transform.SetParent(mpContent.transform, false);
        RectTransform mpBtnRowRect = mpBtnRow.GetComponent<RectTransform>();
        mpBtnRowRect.sizeDelta = new Vector2(320, 42);
        HorizontalLayoutGroup mpHLayout = mpBtnRow.AddComponent<HorizontalLayoutGroup>();
        mpHLayout.spacing = 15;
        mpHLayout.childAlignment = TextAnchor.MiddleCenter;
        mpHLayout.childControlWidth = true;
        mpHLayout.childControlHeight = true;

        GameObject btnHost = CreateFlatButton("HostButton", mpBtnRow.transform, buttonBgColor, buttonTextColor, "CREATE HOST");
        GameObject btnJoin = CreateFlatButton("JoinButton", mpBtnRow.transform, buttonBgColor, buttonTextColor, "JOIN ROOM");

        // Status Text
        GameObject mpStatusObj = new GameObject("StatusTextTMP", typeof(RectTransform));
        mpStatusObj.transform.SetParent(mpContent.transform, false);
        RectTransform mpStatusRect = mpStatusObj.GetComponent<RectTransform>();
        mpStatusRect.sizeDelta = new Vector2(320, 24);
        TMP_Text mpStatus = mpStatusObj.AddComponent<TextMeshProUGUI>();
        mpStatus.text = "Services ready";
        mpStatus.fontSize = 14;
        mpStatus.color = Color.gray;
        mpStatus.alignment = TextAlignmentOptions.Center;

        // Display Code Text
        GameObject mpCodeObj = new GameObject("RoomCodeDisplayTMP", typeof(RectTransform));
        mpCodeObj.transform.SetParent(mpContent.transform, false);
        RectTransform mpCodeRect = mpCodeObj.GetComponent<RectTransform>();
        mpCodeRect.sizeDelta = new Vector2(320, 28);
        TMP_Text mpCode = mpCodeObj.AddComponent<TextMeshProUGUI>();
        mpCode.text = "Room code: -";
        mpCode.fontSize = 16;
        mpCode.fontStyle = FontStyles.Bold;
        mpCode.color = Color.green;
        mpCode.alignment = TextAlignmentOptions.Center;
        mpCode.gameObject.SetActive(false);

        // Disconnect Button
        GameObject btnDisconnect = CreateFlatButton("DisconnectButton", mpContent.transform, buttonBgColor, buttonTextColor, "DISCONNECT");
        RectTransform mpDiscRect = btnDisconnect.GetComponent<RectTransform>();
        mpDiscRect.sizeDelta = new Vector2(320, 42);

        // Multiplayer Back Button
        GameObject btnMpBack = CreateFlatButton("ButtonBack", mpPanel.transform, buttonBgColor, buttonTextColor, "BACK");
        RectTransform mpBackRect = btnMpBack.GetComponent<RectTransform>();
        mpBackRect.anchorMin = new Vector2(1f, 0);
        mpBackRect.anchorMax = new Vector2(1f, 0);
        mpBackRect.pivot = new Vector2(1f, 0);
        mpBackRect.anchoredPosition = new Vector2(-20, 20);
        mpBackRect.sizeDelta = new Vector2(120, 36);

        // Attach NetworkUI script
        NetworkUI netUIScript = mpPanel.AddComponent<NetworkUI>();
        
        // Use SerializedObject to safely link NetworkUI fields
        SerializedObject serNetUI = new SerializedObject(netUIScript);
        serNetUI.FindProperty("joinCodeInputField").objectReferenceValue = mpInputField;
        serNetUI.FindProperty("statusText").objectReferenceValue = mpStatus;
        serNetUI.FindProperty("joinCodeDisplayText").objectReferenceValue = mpCode;
        serNetUI.FindProperty("hostButton").objectReferenceValue = btnHost.GetComponent<Button>();
        serNetUI.FindProperty("joinButton").objectReferenceValue = btnJoin.GetComponent<Button>();
        serNetUI.FindProperty("disconnectButton").objectReferenceValue = btnDisconnect.GetComponent<Button>();
        serNetUI.ApplyModifiedProperties();

        // ====================================================================
        // D. PLACEHOLDER PANELS (1 to 4)
        // ====================================================================
        GameObject[] placeholders = new GameObject[4];
        string[] placeholderNames = { "Placeholder1", "Placeholder2", "Placeholder3", "Placeholder4" };
        string[] placeholderTitles = { "VIDEO SETTINGS", "AUDIO SETTINGS", "GAMEPLAY OPTIONS", "CREDITS & ABOUT" };

        for (int i = 0; i < 4; i++) {
            GameObject pPanel = new GameObject(placeholderNames[i], typeof(RectTransform));
            pPanel.transform.SetParent(containerGo.transform, false);
            RectTransform pRect = pPanel.GetComponent<RectTransform>();
            pRect.anchorMin = new Vector2(0.5f, 0.5f);
            pRect.anchorMax = new Vector2(0.5f, 0.5f);
            pRect.anchoredPosition = Vector2.zero;
            pRect.sizeDelta = new Vector2(700, 520);

            pPanel.AddComponent<CanvasRenderer>();
            Image pImg = pPanel.AddComponent<Image>();
            pImg.color = panelBgColor;

            // Title
            GameObject pTitle = new GameObject("TitleTMP", typeof(RectTransform));
            pTitle.transform.SetParent(pPanel.transform, false);
            RectTransform pTitleRect = pTitle.GetComponent<RectTransform>();
            pTitleRect.anchorMin = new Vector2(0.5f, 1f);
            pTitleRect.anchorMax = new Vector2(0.5f, 1f);
            pTitleRect.pivot = new Vector2(0.5f, 1f);
            pTitleRect.anchoredPosition = new Vector2(0, -35);
            pTitleRect.sizeDelta = new Vector2(500, 40);
            TMP_Text pTitleText = pTitle.AddComponent<TextMeshProUGUI>();
            pTitleText.text = placeholderTitles[i];
            pTitleText.fontSize = 28;
            pTitleText.fontStyle = FontStyles.Bold;
            pTitleText.color = labelTextColor;
            pTitleText.alignment = TextAlignmentOptions.Center;

            // Center Text
            GameObject pMsg = new GameObject("MessageTMP", typeof(RectTransform));
            pMsg.transform.SetParent(pPanel.transform, false);
            RectTransform pMsgRect = pMsg.GetComponent<RectTransform>();
            pMsgRect.anchorMin = new Vector2(0.5f, 0.5f);
            pMsgRect.anchorMax = new Vector2(0.5f, 0.5f);
            pMsgRect.anchoredPosition = Vector2.zero;
            pMsgRect.sizeDelta = new Vector2(500, 50);
            TMP_Text pMsgText = pMsg.AddComponent<TextMeshProUGUI>();
            pMsgText.text = "This feature is currently under development.";
            pMsgText.fontSize = 16;
            pMsgText.fontStyle = FontStyles.Italic;
            pMsgText.color = Color.gray;
            pMsgText.alignment = TextAlignmentOptions.Center;

            // Back button
            GameObject btnPBack = CreateFlatButton("ButtonBack", pPanel.transform, buttonBgColor, buttonTextColor, "BACK");
            RectTransform pBackRect = btnPBack.GetComponent<RectTransform>();
            pBackRect.anchorMin = new Vector2(1f, 0);
            pBackRect.anchorMax = new Vector2(1f, 0);
            pBackRect.pivot = new Vector2(1f, 0);
            pBackRect.anchoredPosition = new Vector2(-20, 20);
            pBackRect.sizeDelta = new Vector2(120, 36);

            // Hook up back button to GameMenuManager.GoBack
            UnityEventTools.AddVoidPersistentListener(btnPBack.GetComponent<Button>().onClick, menuManager.GoBack);

            placeholders[i] = pPanel;
        }

        // ====================================================================
        // E. PREFAB BUILDING & ADDITIONAL BINDINGS
        // ====================================================================
        CreateBWPrefabs(uiScript);

        // Hook up sub-panel navigation buttons on Main Pause Panel
        UnityEventTools.AddObjectPersistentListener(btnControls.GetComponent<Button>().onClick, menuManager.OpenSubPanel, kbPanel);
        UnityEventTools.AddObjectPersistentListener(btnMultiplayer.GetComponent<Button>().onClick, menuManager.OpenSubPanel, mpPanel);
        UnityEventTools.AddObjectPersistentListener(btnPlaceholder1.GetComponent<Button>().onClick, menuManager.OpenSubPanel, placeholders[0]);
        UnityEventTools.AddObjectPersistentListener(btnPlaceholder2.GetComponent<Button>().onClick, menuManager.OpenSubPanel, placeholders[1]);
        UnityEventTools.AddObjectPersistentListener(btnPlaceholder3.GetComponent<Button>().onClick, menuManager.OpenSubPanel, placeholders[2]);
        UnityEventTools.AddObjectPersistentListener(btnPlaceholder4.GetComponent<Button>().onClick, menuManager.OpenSubPanel, placeholders[3]);

        // Hook up main controls/resume/quit
        UnityEventTools.AddVoidPersistentListener(btnResume.GetComponent<Button>().onClick, menuManager.ResumeGame);
        UnityEventTools.AddVoidPersistentListener(btnQuit.GetComponent<Button>().onClick, menuManager.QuitGame);

        // Hook up back buttons inside Controls & Multiplayer submenus
        UnityEventTools.AddVoidPersistentListener(btnKbBack.GetComponent<Button>().onClick, menuManager.GoBack);
        UnityEventTools.AddVoidPersistentListener(btnMpBack.GetComponent<Button>().onClick, menuManager.GoBack);

        // Deactivate the container and all panels initially
        containerGo.SetActive(false);
        mainPanel.SetActive(false);
        kbPanel.SetActive(false);
        mpPanel.SetActive(false);
        foreach (var p in placeholders) p.SetActive(false);

        // ====================================================================
        // F. SERIALIZE AND ASSIGN IN SCENE
        // ====================================================================
        SerializedObject serMenuManager = new SerializedObject(menuManager);
        serMenuManager.FindProperty("menuContainer").objectReferenceValue = containerGo;
        serMenuManager.FindProperty("menuPanel").objectReferenceValue = mainPanel;
        serMenuManager.FindProperty("keyBindingPanel").objectReferenceValue = kbPanel;
        serMenuManager.FindProperty("multiplayerPanel").objectReferenceValue = mpPanel;
        serMenuManager.FindProperty("placeholder1").objectReferenceValue = placeholders[0];
        serMenuManager.FindProperty("placeholder2").objectReferenceValue = placeholders[1];
        serMenuManager.FindProperty("placeholder3").objectReferenceValue = placeholders[2];
        serMenuManager.FindProperty("placeholder4").objectReferenceValue = placeholders[3];
        serMenuManager.ApplyModifiedProperties();

        EditorUtility.SetDirty(menuManager);
        EditorUtility.SetDirty(uiScript);
        EditorUtility.SetDirty(netUIScript);
        EditorUtility.SetDirty(containerGo);

        EditorUtility.DisplayDialog("Success", "Pause Menu & Sub-Panels created successfully!", "Excellent");
    }

    private static GameObject CreateFlatButton(string name, Transform parent, Color bgColor, Color textColor, string labelText) {
        GameObject btnObj = new GameObject(name, typeof(RectTransform));
        btnObj.transform.SetParent(parent, false);
        btnObj.AddComponent<CanvasRenderer>();
        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;
        Button btn = btnObj.AddComponent<Button>();

        GameObject textObj = new GameObject("Text", typeof(RectTransform));
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        TMP_Text tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = labelText;
        tmp.fontSize = 15;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;

        return btnObj;
    }

    private static void CreateBWPrefabs(KeyBindingUI uiScript) {
        string folderPath = "Assets/Prefabs/UI";
        if (!System.IO.Directory.Exists(folderPath)) {
            System.IO.Directory.CreateDirectory(folderPath);
            AssetDatabase.Refresh();
        }

        string rowPath = folderPath + "/KeyBindRowPrefab.prefab";
        string headerPath = folderPath + "/KeyBindHeaderPrefab.prefab";

        // 1. Create Row Prefab
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

        // ActionName
        GameObject nameObj = new GameObject("ActionName", typeof(RectTransform));
        nameObj.transform.SetParent(rowRoot.transform, false);
        TMP_Text nameText = nameObj.AddComponent<TextMeshProUGUI>();
        nameText.text = "Action Name";
        nameText.fontSize = 15;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.Left;
        LayoutElement layoutElement = nameObj.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;

        // KeyButton (Flat White, Black Text)
        GameObject buttonObj = new GameObject("KeyButton", typeof(RectTransform));
        buttonObj.transform.SetParent(rowRoot.transform, false);
        RectTransform btnRect = buttonObj.GetComponent<RectTransform>();
        btnRect.sizeDelta = new Vector2(130, 32);

        buttonObj.AddComponent<CanvasRenderer>();
        Image btnImg = buttonObj.AddComponent<Image>();
        btnImg.color = Color.white;
        buttonObj.AddComponent<Button>();

        GameObject keyTextObj = new GameObject("KeyText", typeof(RectTransform));
        keyTextObj.transform.SetParent(buttonObj.transform, false);
        TMP_Text keyText = keyTextObj.AddComponent<TextMeshProUGUI>();
        keyText.text = "Key";
        keyText.fontSize = 13;
        keyText.fontStyle = FontStyles.Bold;
        keyText.color = Color.black;
        keyText.alignment = TextAlignmentOptions.Center;

        RectTransform textRect = keyTextObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        PrefabUtility.SaveAsPrefabAsset(rowRoot, rowPath);
        DestroyImmediate(rowRoot);

        // 2. Create Header Prefab
        GameObject headerRoot = new GameObject("KeyBindHeaderPrefab", typeof(RectTransform));
        RectTransform headerRect = headerRoot.GetComponent<RectTransform>();
        headerRect.sizeDelta = new Vector2(400, 35);

        GameObject textObj = new GameObject("HeaderText", typeof(RectTransform));
        textObj.transform.SetParent(headerRoot.transform, false);
        TMP_Text headerText = textObj.AddComponent<TextMeshProUGUI>();
        headerText.text = "CATEGORY";
        headerText.fontSize = 16;
        headerText.fontStyle = FontStyles.Bold;
        headerText.color = new Color(0.7f, 0.7f, 0.7f, 1f); // Muted gray category title
        headerText.alignment = TextAlignmentOptions.Left;

        RectTransform textRect2 = textObj.GetComponent<RectTransform>();
        textRect2.anchorMin = Vector2.zero;
        textRect2.anchorMax = Vector2.one;
        textRect2.sizeDelta = new Vector2(-10, 0);

        PrefabUtility.SaveAsPrefabAsset(headerRoot, headerPath);
        DestroyImmediate(headerRoot);

        // Link
        uiScript.rowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rowPath);
        uiScript.categoryHeaderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(headerPath);
        AssetDatabase.Refresh();
    }
}
