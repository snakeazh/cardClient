using UnityEngine;

namespace App.UI.Effects
{
    /// <summary>UGUI 巡逻落点；相邻点限定可行走路线，避免穿过建筑。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIRabbitWaypoint : MonoBehaviour
    {
        [SerializeField] private bool eatHere;
        [SerializeField] private UIRabbitWaypoint[] neighbours;

        public bool EatHere => eatHere;
        public UIRabbitWaypoint[] Neighbours => neighbours;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = eatHere ? Color.green : Color.cyan;
            var rect = (RectTransform)transform;
            var size = rect.TransformVector(new Vector3(24f, 24f, 0f));
            Gizmos.DrawWireCube(transform.position, size);
            if (neighbours == null) return;
            foreach (var neighbour in neighbours)
            {
                if (neighbour != null) Gizmos.DrawLine(transform.position, neighbour.transform.position);
            }
            UnityEditor.Handles.Label(transform.position, name + (eatHere ? " (吃草)" : " (等待)"));
        }
#endif
    }
}
