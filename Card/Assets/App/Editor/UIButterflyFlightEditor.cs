using App.UI.Effects;
using UnityEditor;
using UnityEngine;

namespace App.Editor
{
    [CustomEditor(typeof(UIButterflyFlight))]
    public sealed class UIButterflyFlightEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var rect = ((UIButterflyFlight)target).transform as RectTransform;
            var parent = rect != null ? rect.parent as RectTransform : null;
            if (parent == null) return;

            serializedObject.Update();
            var points = serializedObject.FindProperty("patrolPoints");
            var anchor = new Vector2(
                Mathf.Lerp(rect.anchorMin.x, rect.anchorMax.x, rect.pivot.x),
                Mathf.Lerp(rect.anchorMin.y, rect.anchorMax.y, rect.pivot.y));
            var origin = parent.rect.min + Vector2.Scale(parent.rect.size, anchor);
            var oldColor = Handles.color;
            Handles.color = Color.yellow;
            for (var i = 0; i < points.arraySize; i++)
            {
                var point = points.GetArrayElementAtIndex(i);
                var world = parent.TransformPoint(origin + point.vector2Value);
                var size = HandleUtility.GetHandleSize(world) * 0.07f;
                EditorGUI.BeginChangeCheck();
                var moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                    point.vector2Value = (Vector2)parent.InverseTransformPoint(moved) - origin;
                Handles.Label(world, $"巡逻点 {i + 1}");
            }
            Handles.color = oldColor;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
