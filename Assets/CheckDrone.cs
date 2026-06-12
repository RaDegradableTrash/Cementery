using UnityEngine;
public class CheckDrone : MonoBehaviour {
    void Start() {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/DJIInspire3.prefab");
        if (prefab != null) {
            Camera[] cams = prefab.GetComponentsInChildren<Camera>(true);
            Debug.Log("FOUND CAMS: " + cams.Length);
            foreach (Camera c in cams) Debug.Log("CAM: " + c.gameObject.name);
        } else {
            Debug.Log("PREFAB NOT FOUND");
        }
    }
}
