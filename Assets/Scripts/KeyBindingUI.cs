using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class KeyBindingUI : MonoBehaviour {
    [Header("UI References")]
    [Tooltip("Prefab containing ActionName (TMP_Text) and KeyButton (Button with TMP_Text child)")]
    public GameObject rowPrefab;
    [Tooltip("Prefab containing a single TMP_Text for Category Headers")]
    public GameObject categoryHeaderPrefab;
    public Transform container;
    
    [Header("Status & Warning Feedbacks")]
    public TMP_Text statusText;
    
    [Header("Preset & Profile Selection")]
    public TMP_Dropdown profileDropdown;
    
    [Header("Import / Export Inputs")]
    public TMP_InputField importExportFileNameInput; // default is 'keybinds_default.txt'
    public Button exportButton;
    public Button importButton;
    public Button resetDefaultButton;

    public static bool IsRebinding = false;

    private bool isListening = false;
    private BindingItem activeRebindItem = null;
    private Button activeRebindButton = null;

    void Start() {
        InitializeProfileDropdown();
        RefreshUI();

        // Wire buttons
        if (exportButton != null) exportButton.onClick.AddListener(ExportActiveProfile);
        if (importButton != null) importButton.onClick.AddListener(ImportProfile);
        if (resetDefaultButton != null) resetDefaultButton.onClick.AddListener(() => {
            InputManager.Instance.ResetToDefault();
            RefreshUI();
        });
        if (profileDropdown != null) profileDropdown.onValueChanged.AddListener(OnProfileDropdownChanged);
    }

    void OnDisable() {
        StopAllCoroutines();
        isListening = false;
        IsRebinding = false;
        activeRebindItem = null;
        activeRebindButton = null;
    }

    private void InitializeProfileDropdown() {
        if (profileDropdown == null) return;

        profileDropdown.ClearOptions();
        List<string> options = new List<string>();
        foreach (var preset in InputManager.Instance.PresetProfiles) {
            options.Add(preset.ProfileName);
        }
        profileDropdown.AddOptions(options);

        // Set current selection value
        int currentIndex = InputManager.Instance.PresetProfiles.FindIndex(
            p => p.ProfileName == InputManager.Instance.CurrentProfileName
        );
        if (currentIndex >= 0) {
            profileDropdown.value = currentIndex;
        }
    }

    private void OnProfileDropdownChanged(int index) {
        if (index < 0 || index >= InputManager.Instance.PresetProfiles.Count) return;
        string selectedProfile = InputManager.Instance.PresetProfiles[index].ProfileName;
        InputManager.Instance.ApplyPreset(selectedProfile);
        ShowStatus($"Switched to profile: {selectedProfile}", Color.green);
        RefreshUI();
    }

    public void RefreshUI() {
        // Clear container
        foreach (Transform child in container) {
            Destroy(child.gameObject);
        }

        // Find duplicate bound keys
        Dictionary<KeyCode, int> keyCounts = new Dictionary<KeyCode, int>();
        foreach (var binding in InputManager.Instance.ActiveBindings) {
            if (binding.Key != KeyCode.None) {
                if (keyCounts.ContainsKey(binding.Key)) {
                    keyCounts[binding.Key]++;
                } else {
                    keyCounts[binding.Key] = 1;
                }
            }
        }

        // Group bindings by category for Minecraft-like visual structure
        Dictionary<string, List<BindingItem>> categorizedBindings = new Dictionary<string, List<BindingItem>>();
        foreach (var binding in InputManager.Instance.ActiveBindings) {
            string cat = string.IsNullOrEmpty(binding.Category) ? "Other" : binding.Category;
            if (!categorizedBindings.ContainsKey(cat)) {
                categorizedBindings[cat] = new List<BindingItem>();
            }
            categorizedBindings[cat].Add(binding);
        }

        // Instantiate header + rows
        foreach (var category in categorizedBindings) {
            // Instantiate Header
            if (categoryHeaderPrefab != null) {
                GameObject header = Instantiate(categoryHeaderPrefab, container);
                TMP_Text headerText = header.GetComponentInChildren<TMP_Text>();
                if (headerText != null) {
                    headerText.text = category.Key.ToUpper();
                    headerText.fontStyle = FontStyles.Bold;
                    headerText.fontSize = 18;
                    headerText.color = Color.white; // Ensure high-contrast white text on dark bg
                }
            }

            // Instantiate rows for each action in this category
            foreach (var binding in category.Value) {
                GameObject row = Instantiate(rowPrefab, container);
                
                bool hasConflict = binding.Key != KeyCode.None && keyCounts.ContainsKey(binding.Key) && keyCounts[binding.Key] > 1;
                Color textColor = hasConflict ? new Color(1f, 0.45f, 0.45f, 1f) : Color.white; // Light red on dark bg if duplicate
                Color btnTextColor = hasConflict ? new Color(1f, 0.45f, 0.45f, 1f) : Color.black; // Light red on white button if duplicate
                
                Transform nameTrans = row.transform.Find("ActionName");
                if (nameTrans != null) {
                    TMP_Text nameText = nameTrans.GetComponent<TMP_Text>();
                    if (nameText != null) {
                        nameText.text = binding.ActionName;
                        nameText.fontStyle = FontStyles.Bold;
                        nameText.fontSize = 16;
                        nameText.color = textColor;
                    }
                }

                Transform btnTrans = row.transform.Find("KeyButton");
                if (btnTrans != null) {
                    Button btn = btnTrans.GetComponent<Button>();
                    if (btn != null) {
                        TMP_Text btnText = btn.GetComponentInChildren<TMP_Text>();
                        if (btnText != null) {
                            btnText.text = binding.Key.ToString();
                            btnText.fontStyle = FontStyles.Bold;
                            btnText.fontSize = 14;
                            btnText.color = btnTextColor;
                        }
                        
                        // Copy binding reference to avoid closure issue
                        BindingItem currentItem = binding;
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() => StartRebind(currentItem, btn));
                    }
                }
            }
        }
    }

    void StartRebind(BindingItem item, Button btn) {
        if (isListening) return;
        
        isListening = true;
        IsRebinding = true;
        activeRebindItem = item;
        activeRebindButton = btn;
        
        TMP_Text btnText = btn.GetComponentInChildren<TMP_Text>();
        if (btnText != null) {
            btnText.text = $"<u>{item.Key.ToString()}</u>"; // Underline format when active
            btnText.fontStyle = FontStyles.Bold | FontStyles.Underline;
        }
        
        ShowStatus("Press any key or mouse click to rebind. Press ESC to cancel.", Color.yellow);
        StartCoroutine(WaitForInput());
    }

    IEnumerator WaitForInput() {
        // 1. Skip the frame in which the click occurred to prevent instant self-triggering
        yield return null;

        // 2. Wait until all mouse clicks are fully released
        while (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)) {
            yield return null;
        }

        // 3. Now wait until a new key is pressed or mouse click is detected
        yield return new WaitUntil(() => Input.anyKeyDown || 
            Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2));

        KeyCode pressedKey = KeyCode.None;
        if (Input.GetMouseButtonDown(0)) {
            pressedKey = KeyCode.Mouse0;
        } else if (Input.GetMouseButtonDown(1)) {
            pressedKey = KeyCode.Mouse1;
        } else if (Input.GetMouseButtonDown(2)) {
            pressedKey = KeyCode.Mouse2;
        } else {
            foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode))) {
                if (Input.GetKeyDown(k)) {
                    pressedKey = k;
                    break;
                }
            }
        }

        if (pressedKey == KeyCode.Escape) {
            ShowStatus("Rebinding cancelled.", Color.white);
            RefreshUI();
        } else if (pressedKey != KeyCode.None) {
            // Apply new key bind
            activeRebindItem.Key = pressedKey;
            InputManager.Instance.SaveConfig();
            
            // Re-render UI to update conflict colors
            RefreshUI();

            // Report duplicate warning in status text
            BindingItem conflictItem;
            if (InputManager.Instance.CheckConflict(pressedKey, activeRebindItem, out conflictItem)) {
                ShowStatus($"Warning: Key '{pressedKey}' is duplicated (also used by '{conflictItem.ActionName}').", new Color(1f, 0.45f, 0.45f, 1f));
            } else {
                ShowStatus($"Rebound '{activeRebindItem.ActionName}' to {pressedKey}.", Color.green);
            }
        } else {
            RefreshUI();
        }

        // Wait until end of frame to ensure the input event is fully consumed in the current update loops
        yield return new WaitForEndOfFrame();

        isListening = false;
        IsRebinding = false;
        activeRebindItem = null;
        activeRebindButton = null;
    }

    public void ExportActiveProfile() {
        string customName = "";
        if (importExportFileNameInput != null && !string.IsNullOrEmpty(importExportFileNameInput.text)) {
            customName = importExportFileNameInput.text.Trim();
            if (customName.EndsWith(".txt")) {
                customName = customName.Substring(0, customName.Length - 4);
            }
        }
        
        InputManager.Instance.ExportProfileToTxt(customName);
        string profileName = string.IsNullOrEmpty(customName) ? InputManager.Instance.CurrentProfileName : customName;
        string fileName = $"keybinds_{profileName.ToLower().Replace(" ", "_")}.txt";
        ShowStatus($"Exported layout to Downloads/{fileName}", Color.green);
    }

    public void ImportProfile() {
        if (importExportFileNameInput == null || string.IsNullOrEmpty(importExportFileNameInput.text)) {
            ShowStatus("Please enter a profile filename to import.", Color.red);
            return;
        }

        string fileName = importExportFileNameInput.text.Trim();
        if (!fileName.EndsWith(".txt")) {
            fileName += ".txt";
        }

        if (InputManager.Instance.ImportProfileFromTxt(fileName)) {
            ShowStatus($"Imported profile layout from Downloads/{fileName}", Color.green);
            InitializeProfileDropdown();
            RefreshUI();
        } else {
            ShowStatus($"File '{fileName}' not found in Downloads directory.", Color.red);
        }
    }

    private void ShowStatus(string message, Color color) {
        if (statusText != null) {
            statusText.text = message;
            statusText.color = color;
        }
    }
}
