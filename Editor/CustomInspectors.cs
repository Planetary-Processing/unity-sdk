using System.Collections.Generic;
using System.Reflection;
using System;
using UnityEditor;
using UnityEngine;
using Planetary;

[CustomEditor(typeof(PPPlayer))]
public class PlayerEditor : Editor
{
    private List<SerializedProperty> properties = new List<SerializedProperty>();

    void OnEnable() {
        string[] hiddenProperties = new string[]{"Type"};
        IEnumerable<FieldInfo> fields = serializedObject.targetObject.GetType().GetFields();
        foreach (FieldInfo fi in fields) {
            if (fi.IsPublic && !Attribute.IsDefined(fi, typeof(HideInInspector)) && fi.Name != "Type") {
                properties.Add(serializedObject.FindProperty(fi.Name));
            }
        }
    }

    public override void OnInspectorGUI() {
        foreach (SerializedProperty property in properties)
        {
            EditorGUILayout.PropertyField(property,true);
        }
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(PPEntity))]
public class EntityEditor : Editor
{
    private List<SerializedProperty> properties = new List<SerializedProperty>();

    void OnEnable() {
        IEnumerable<FieldInfo> fields = serializedObject.targetObject.GetType().GetFields();
        foreach (FieldInfo fi in fields) {
            if (fi.IsPublic && !Attribute.IsDefined(fi, typeof(HideInInspector))) {
                properties.Add(serializedObject.FindProperty(fi.Name));
            }
        }
    }

    public override void OnInspectorGUI() {
        foreach (SerializedProperty property in properties)
        {
            EditorGUILayout.PropertyField(property,true);
        }
        serializedObject.ApplyModifiedProperties();
    }
}
