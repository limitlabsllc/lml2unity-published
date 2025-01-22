using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LMLAssetLicense : MonoBehaviour
{
    [HideInInspector]
    public string attribution;

#if UNITY_EDITOR
    [CustomEditor(typeof(LMLAssetLicense))]
    public class LMLAssetLicenseEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            LMLAssetLicense license = (LMLAssetLicense)target;

            EditorGUILayout.LabelField("Attribution/License:", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(license.attribution, EditorStyles.textArea,
                GUILayout.MinHeight(80));
        }
    }
#endif
}