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
