using UnityEngine;
using System.Collections.Generic;

public class GameMenuManager : MonoBehaviour
{
    [Header("Menu Container")]
    [SerializeField] private GameObject menuContainer; // The parent container for all menu panels

    [Header("UI Panels")]
    [SerializeField] private GameObject menuPanel; // This will act as the Main Pause Panel
    [SerializeField] private GameObject keyBindingPanel;
    [SerializeField] private GameObject multiplayerPanel;
    [SerializeField] private GameObject placeholder1;
    [SerializeField] private GameObject placeholder2;
    [SerializeField] private GameObject placeholder3;
    [SerializeField] private GameObject placeholder4;
    
    public static bool IsMenuOpen = false;
    public static int ClosedFrameCount = -1;

    // Use a list/stack to keep track of menu navigation history
    private List<GameObject> menuHistory = new List<GameObject>();

    void Awake()
    {
        IsMenuOpen = false;
        ResolveReferencesAndBindings();
    }

    private void ResolveReferencesAndBindings()
    {
        if (menuContainer == null)
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                Transform containerTrans = canvas.transform.Find("PauseMenuContainer");
                if (containerTrans != null)
                {
                    menuContainer = containerTrans.gameObject;
                    
                    if (menuPanel == null)
                    {
                        Transform t = containerTrans.Find("MainPausePanel");
                        if (t != null) menuPanel = t.gameObject;
                    }
                    if (keyBindingPanel == null)
                    {
                        Transform t = containerTrans.Find("KeyBindingPanel");
                        if (t != null) keyBindingPanel = t.gameObject;
                    }
                    if (multiplayerPanel == null)
                    {
                        Transform t = containerTrans.Find("MultiplayerPanel");
                        if (t != null) multiplayerPanel = t.gameObject;
                    }
                    if (placeholder1 == null)
                    {
                        Transform t = containerTrans.Find("Placeholder1");
                        if (t != null) placeholder1 = t.gameObject;
                    }
                    if (placeholder2 == null)
                    {
                        Transform t = containerTrans.Find("Placeholder2");
                        if (t != null) placeholder2 = t.gameObject;
                    }
                    if (placeholder3 == null)
                    {
                        Transform t = containerTrans.Find("Placeholder3");
                        if (t != null) placeholder3 = t.gameObject;
                    }
                    if (placeholder4 == null)
                    {
                        Transform t = containerTrans.Find("Placeholder4");
                        if (t != null) placeholder4 = t.gameObject;
                    }
                }
            }
        }

        // Dynamically bind standard buttons if they are found under the panels to ensure they work at runtime
        if (menuContainer != null)
        {
            // Main Pause Panel buttons
            BindButtonAction(menuPanel, "ButtonControls", () => OpenSubPanel(keyBindingPanel));
            BindButtonAction(menuPanel, "ButtonMultiplayer", () => OpenSubPanel(multiplayerPanel));
            BindButtonAction(menuPanel, "ButtonPlaceholder1", () => OpenSubPanel(placeholder1));
            BindButtonAction(menuPanel, "ButtonPlaceholder2", () => OpenSubPanel(placeholder2));
            BindButtonAction(menuPanel, "ButtonPlaceholder3", () => OpenSubPanel(placeholder3));
            BindButtonAction(menuPanel, "ButtonPlaceholder4", () => OpenSubPanel(placeholder4));
            BindButtonAction(menuPanel, "ButtonResume", ResumeGame);
            BindButtonAction(menuPanel, "ButtonQuit", QuitGame);

            // Back buttons in sub panels
            BindButtonAction(keyBindingPanel, "ButtonBack", GoBack);
            BindButtonAction(multiplayerPanel, "ButtonBack", GoBack);
            BindButtonAction(placeholder1, "ButtonBack", GoBack);
            BindButtonAction(placeholder2, "ButtonBack", GoBack);
            BindButtonAction(placeholder3, "ButtonBack", GoBack);
            BindButtonAction(placeholder4, "ButtonBack", GoBack);
        }
    }

    private void BindButtonAction(GameObject parentPanel, string buttonName, UnityEngine.Events.UnityAction action)
    {
        if (parentPanel == null) return;
        Transform btnTrans = parentPanel.transform.Find(buttonName);
        if (btnTrans == null)
        {
            // Try recursive find in case it's in a grid or layout row
            btnTrans = FindDeepChild(parentPanel.transform, buttonName);
        }

        if (btnTrans != null)
        {
            UnityEngine.UI.Button btn = btnTrans.GetComponent<UnityEngine.UI.Button>();
            if (btn != null)
            {
                btn.onClick.RemoveListener(action);
                btn.onClick.AddListener(action);
            }
        }
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }


    void Start()
    {
        // Deactivate the entire container at start
        if (menuContainer != null)
        {
            menuContainer.SetActive(false);
        }
        CloseMenu();
    }

    void Update()
    {
        // If a rebind is in progress, let the rebind code handle Escape/inputs
        if (KeyBindingUI.IsRebinding) return;

        // Toggle or navigate back on Escape/P/Return/KeypadEnter
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (PlayerDeathFlowController.IsPlayerDead) return;

            if (IsMenuOpen)
            {
                // If we are deep in sub-menus, go back one level
                if (menuHistory.Count > 1)
                {
                    GoBack();
                }
                else
                {
                    // Otherwise close the whole menu
                    CloseMenu();
                }
            }
            else
            {
                OpenMenu();
            }
        }
    }

    public void OpenMenu()
    {
        IsMenuOpen = true;
        menuHistory.Clear();

        // Show the container
        if (menuContainer != null)
        {
            menuContainer.SetActive(true);
        }

        // Deactivate all panels first, then show the Main Pause Panel (menuPanel)
        DeactivateAllPanels();
        
        if (menuPanel != null)
        {
            menuPanel.SetActive(true);
            menuHistory.Add(menuPanel);
        }
        
        // Unlock cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseMenu()
    {
        IsMenuOpen = false;
        ClosedFrameCount = Time.frameCount;
        menuHistory.Clear();
        DeactivateAllPanels();

        // Hide the container
        if (menuContainer != null)
        {
            menuContainer.SetActive(false);
        }
        
        // Lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Open a sub-menu panel, adding it to the history stack and hiding the previous panel
    public void OpenSubPanel(GameObject subPanel)
    {
        if (subPanel == null) return;

        // Hide current active panel if any
        if (menuHistory.Count > 0)
        {
            menuHistory[menuHistory.Count - 1].SetActive(false);
        }

        subPanel.SetActive(true);
        menuHistory.Add(subPanel);
    }

    // Go back to the previous panel in history
    public void GoBack()
    {
        if (menuHistory.Count > 1)
        {
            // Deactivate current panel
            GameObject currentPanel = menuHistory[menuHistory.Count - 1];
            currentPanel.SetActive(false);
            menuHistory.RemoveAt(menuHistory.Count - 1);

            // Activate previous panel
            GameObject previousPanel = menuHistory[menuHistory.Count - 1];
            previousPanel.SetActive(true);
        }
        else
        {
            // If only the main panel is left, ESC/GoBack closes the menu
            CloseMenu();
        }
    }

    private void DeactivateAllPanels()
    {
        if (menuPanel != null) menuPanel.SetActive(false);
        if (keyBindingPanel != null) keyBindingPanel.SetActive(false);
        if (multiplayerPanel != null) multiplayerPanel.SetActive(false);
        if (placeholder1 != null) placeholder1.SetActive(false);
        if (placeholder2 != null) placeholder2.SetActive(false);
        if (placeholder3 != null) placeholder3.SetActive(false);
        if (placeholder4 != null) placeholder4.SetActive(false);
    }

    // Public method for UI buttons to resume
    public void ResumeGame()
    {
        CloseMenu();
    }

    // Public helper for buttons to quit
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
