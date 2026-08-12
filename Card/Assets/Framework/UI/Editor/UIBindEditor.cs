using System.Collections.Generic;
using Framework.UI.Binding;
using UnityEditor;
using UnityEngine;

namespace Framework.UI.Editor
{
    [CustomEditor(typeof(UIBind))]
    public sealed class UIBindEditor : UnityEditor.Editor
    {
        private SerializedProperty _key;
        private SerializedProperty _target;

        private void OnEnable()
        {
            _key = serializedObject.FindProperty("_key");
            _target = serializedObject.FindProperty("_target");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_key, new GUIContent("Key"));

            var bind = (UIBind)target;
            var components = bind.GetComponents<Component>();
            var labels = new List<string>();
            var options = new List<Component>();
            var selected = 0;

            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is Transform || component is UIBind)
                {
                    continue;
                }

                labels.Add(component.GetType().Name);
                options.Add(component);
                if (_target.objectReferenceValue == component)
                {
                    selected = options.Count - 1;
                }
            }

            if (options.Count == 0)
            {
                EditorGUILayout.HelpBox("No selectable components on this GameObject.", MessageType.Warning);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var next = EditorGUILayout.Popup("Target Component", selected, labels.ToArray());
                if (EditorGUI.EndChangeCheck() || _target.objectReferenceValue == null)
                {
                    _target.objectReferenceValue = options[Mathf.Clamp(next, 0, options.Count - 1)];
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(UIReference))]
    public sealed class UIReferenceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var reference = (UIReference)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Collect UIBinds"))
            {
                Undo.RecordObject(reference, "Collect UIBinds");
                reference.Collect();
                EditorUtility.SetDirty(reference);
            }
        }
    }
}
