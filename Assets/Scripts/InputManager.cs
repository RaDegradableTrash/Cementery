using UnityEngine;
using System.Collections.Generic;
using System.IO;

[System.Serializable]
public class BindingItem {
    public string ActionName;
    public KeyCode Key;
    public string Category; // e.g., "Movement", "Interaction", "Drone", etc.
}

[System.Serializable]
public class KeybindProfile {
    public string ProfileName;
    public List<BindingItem> Bindings = new List<BindingItem>();
}

public class InputManager : MonoBehaviour {
    private static InputManager _instance;
    public static InputManager Instance {
        get {
            if (_instance == null) {
                _instance = FindObjectOfType<InputManager>();
                if (_instance == null) {
                    GameObject go = GameObject.Find("ManagerParent");
                    if (go == null) {
                        go = new GameObject("InputManager");
                    }
                    _instance = go.AddComponent<InputManager>();
                }
            }
            return _instance;
        }
    }

    [Header("Profiles & Active Bindings")]
    public string CurrentProfileName = "Default";
    public List<BindingItem> ActiveBindings = new List<BindingItem>();

    [Header("Preset Profiles")]
    public List<KeybindProfile> PresetProfiles = new List<KeybindProfile>();

    void Awake() {
        if (_instance == null) {
            _instance = this;
            if (transform.parent == null) {
                DontDestroyOnLoad(gameObject);
            }
        } else if (_instance != this) {
            Destroy(gameObject);
            return;
        }

        InitializeDefaultPresets();
        LoadConfig();
    }

    void Reset() {
        InitializePresetsContextMenu();
    }

    [ContextMenu("Initialize Presets")]
    public void InitializePresetsContextMenu() {
        PresetProfiles.Clear();
        InitializeDefaultPresets();
        ApplyPreset(PresetProfiles[0].ProfileName);
    }

    private void InitializeDefaultPresets() {
        // Create standard Default preset if none exist
        if (PresetProfiles.Count == 0) {
            var defaultProfile = new KeybindProfile { ProfileName = "Default" };
            // Movement Group
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Move Forward", Key = KeyCode.W, Category = "Movement" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Move Backward", Key = KeyCode.S, Category = "Movement" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Move Left", Key = KeyCode.A, Category = "Movement" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Move Right", Key = KeyCode.D, Category = "Movement" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Jump", Key = KeyCode.Space, Category = "Movement" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Sprint", Key = KeyCode.LeftShift, Category = "Movement" });
            
            // Interaction Group
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Interact / Collect", Key = KeyCode.E, Category = "Interaction" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Toggle Inventory", Key = KeyCode.Tab, Category = "Interaction" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Rotate Carried Item", Key = KeyCode.R, Category = "Interaction" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Drop Item", Key = KeyCode.Q, Category = "Interaction" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Interact Drone / Corpse", Key = KeyCode.F, Category = "Interaction" });
            
            // Drone Control Group
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Drone Fly Up", Key = KeyCode.Z, Category = "Drone" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Drone Fly Down", Key = KeyCode.C, Category = "Drone" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Drone Turn Left", Key = KeyCode.Q, Category = "Drone" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Drone Turn Right", Key = KeyCode.E, Category = "Drone" });

            // Vehicles & System Group
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Switch Camera", Key = KeyCode.V, Category = "System" });
            defaultProfile.Bindings.Add(new BindingItem { ActionName = "Pause / Menu", Key = KeyCode.Escape, Category = "System" });

            PresetProfiles.Add(defaultProfile);

            // Create alternate preset (e.g., Alternative / Arrow keys)
            var arrowProfile = new KeybindProfile { ProfileName = "Alternative" };
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Move Forward", Key = KeyCode.UpArrow, Category = "Movement" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Move Backward", Key = KeyCode.DownArrow, Category = "Movement" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Move Left", Key = KeyCode.LeftArrow, Category = "Movement" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Move Right", Key = KeyCode.RightArrow, Category = "Movement" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Jump", Key = KeyCode.RightControl, Category = "Movement" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Sprint", Key = KeyCode.RightShift, Category = "Movement" });
            
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Interact / Collect", Key = KeyCode.Return, Category = "Interaction" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Toggle Inventory", Key = KeyCode.I, Category = "Interaction" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Rotate Carried Item", Key = KeyCode.KeypadEnter, Category = "Interaction" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Drop Item", Key = KeyCode.Backspace, Category = "Interaction" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Interact Drone / Corpse", Key = KeyCode.RightAlt, Category = "Interaction" });
            
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Drone Fly Up", Key = KeyCode.PageUp, Category = "Drone" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Drone Fly Down", Key = KeyCode.PageDown, Category = "Drone" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Drone Turn Left", Key = KeyCode.Delete, Category = "Drone" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Drone Turn Right", Key = KeyCode.End, Category = "Drone" });

            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Switch Camera", Key = KeyCode.C, Category = "System" });
            arrowProfile.Bindings.Add(new BindingItem { ActionName = "Pause / Menu", Key = KeyCode.P, Category = "System" });

            PresetProfiles.Add(arrowProfile);
        }
    }

    public void ApplyPreset(string profileName) {
        var preset = PresetProfiles.Find(p => p.ProfileName == profileName);
        if (preset != null) {
            CurrentProfileName = profileName;
            // Deep copy bindings to avoid modifying original preset
            ActiveBindings.Clear();
            foreach (var item in preset.Bindings) {
                ActiveBindings.Add(new BindingItem {
                    ActionName = item.ActionName,
                    Key = item.Key,
                    Category = item.Category
                });
            }
            SaveConfig();
        }
    }

    public void SaveConfig() {
        string path = GetConfigFilePath();
        KeybindProfile activeProfile = new KeybindProfile {
            ProfileName = CurrentProfileName,
            Bindings = ActiveBindings
        };
        string json = JsonUtility.ToJson(activeProfile, true);
        File.WriteAllText(path, json);
    }

    public void LoadConfig() {
        string path = GetConfigFilePath();
        if (File.Exists(path)) {
            try {
                string json = File.ReadAllText(path);
                KeybindProfile profile = JsonUtility.FromJson<KeybindProfile>(json);
                CurrentProfileName = profile.ProfileName;
                ActiveBindings = profile.Bindings;
            } catch (System.Exception e) {
                Debug.LogError("Failed to load keybinds: " + e.Message);
                ResetToDefault();
            }
        } else {
            ResetToDefault();
        }
    }

    public void ResetToDefault() {
        if (PresetProfiles.Count > 0) {
            ApplyPreset(PresetProfiles[0].ProfileName);
        }
    }

    // Export keybind profile as txt file
    public void ExportProfileToTxt(string customName = "") {
        string profileName = string.IsNullOrEmpty(customName) ? CurrentProfileName : customName;
        string downloadsPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");
        if (!System.IO.Directory.Exists(downloadsPath)) {
            System.IO.Directory.CreateDirectory(downloadsPath);
        }
        string fileName = $"keybinds_{profileName.ToLower().Replace(" ", "_")}.txt";
        string fullPath = Path.Combine(downloadsPath, fileName);

        KeybindProfile profile = new KeybindProfile {
            ProfileName = profileName,
            Bindings = ActiveBindings
        };
        string json = JsonUtility.ToJson(profile, true);
        File.WriteAllText(fullPath, json);
        Debug.Log($"Key bindings exported successfully to {fullPath}");
    }

    // Import keybind profile from custom txt file
    public bool ImportProfileFromTxt(string fileName) {
        string downloadsPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");
        string fullPath = Path.Combine(downloadsPath, fileName);

        if (File.Exists(fullPath)) {
            try {
                string json = File.ReadAllText(fullPath);
                KeybindProfile profile = JsonUtility.FromJson<KeybindProfile>(json);
                CurrentProfileName = profile.ProfileName;
                ActiveBindings = profile.Bindings;
                SaveConfig();
                Debug.Log($"Key bindings imported successfully from {fullPath}");
                return true;
            } catch (System.Exception e) {
                Debug.LogError("Failed to import keybind profile: " + e.Message);
            }
        }
        return false;
    }

    // Conflict detection helper
    public bool CheckConflict(KeyCode key, BindingItem currentItem, out BindingItem conflictItem) {
        foreach (var binding in ActiveBindings) {
            if (binding != currentItem && binding.Key == key) {
                conflictItem = binding;
                return true;
            }
        }
        conflictItem = null;
        return false;
    }

    // Helpers to query active input
    public bool GetKeyDown(string actionName) {
        var binding = ActiveBindings.Find(b => b.ActionName == actionName);
        return binding != null && Input.GetKeyDown(binding.Key);
    }

    public bool GetKey(string actionName) {
        var binding = ActiveBindings.Find(b => b.ActionName == actionName);
        return binding != null && Input.GetKey(binding.Key);
    }

    public bool GetKeyUp(string actionName) {
        var binding = ActiveBindings.Find(b => b.ActionName == actionName);
        return binding != null && Input.GetKeyUp(binding.Key);
    }

    private string GetConfigFilePath() {
        return Path.Combine(Application.persistentDataPath, "keybinds_config.json");
    }
}
