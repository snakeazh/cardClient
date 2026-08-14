using System.Collections.Generic;
using Framework.UI.Binding;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

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
            var labels = new List<string>();
            var options = new List<UnityObject>();

            labels.Add("GameObject");
            options.Add(bind.gameObject);

            var components = bind.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is UIBind)
                {
                    continue;
                }

                labels.Add(component.GetType().Name);
                options.Add(component);
            }

            var current = _target.objectReferenceValue;
            var selected = IndexOf(options, current);
            if (selected < 0)
            {
                selected = PreferDefaultIndex(options);
                _target.objectReferenceValue = options[selected];
            }

            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.Popup("Target", selected, labels.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                _target.objectReferenceValue = options[Mathf.Clamp(next, 0, options.Count - 1)];
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static int IndexOf(List<UnityObject> options, UnityObject current)
        {
            if (current == null)
            {
                return -1;
            }

            for (var i = 0; i < options.Count; i++)
            {
                if (options[i] == current)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int PreferDefaultIndex(List<UnityObject> options)
        {
            var transformIndex = -1;
            var gameObjectIndex = -1;
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option is GameObject)
                {
                    gameObjectIndex = i;
                    continue;
                }

                if (option is Transform)
                {
                    transformIndex = i;
                    continue;
                }

                return i;
            }

            if (transformIndex >= 0)
            {
                return transformIndex;
            }

            return gameObjectIndex >= 0 ? gameObjectIndex : 0;
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
