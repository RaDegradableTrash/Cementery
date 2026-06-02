using UnityEditor;
using UnityEngine;

namespace EnvironmentSystem.Editor
{
    [CustomPropertyDrawer(typeof(ReadOnlyPlayModeAttribute))]
    public class ReadOnlyPlayModeDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            bool disableField = Application.isPlaying;
            if (disableField)
            {
                GUI.enabled = false;
            }
            
            EditorGUI.PropertyField(position, property, label, true);
            
            if (disableField)
            {
                GUI.enabled = true;
            }
        }
    }
}
