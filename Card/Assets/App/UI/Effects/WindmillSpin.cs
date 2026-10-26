using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 风车等装饰图持续旋转：绕 RectTransform 的 Pivot 匀速自转。
    /// Ornament26 四叶对称、重心居中，Pivot 保持默认 (0.5, 0.5) 即绕毂心转；
    /// 换成不对称的图时先把 Pivot 挪到视觉轴心，否则会整体画圈。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Windmill Spin")]
    public sealed class WindmillSpin : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("角速度（度/秒），正值逆时针、负值顺时针")]
        private float speed = 60f;

        [SerializeField]
        [Tooltip("忽略 Time.timeScale，暂停时也继续转")]
        private bool unscaledTime = true;

        public float Speed
        {
            get => speed;
            set => speed = value;
        }

        private void Update()
        {
            var delta = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (speed != 0f && delta > 0f)
            {
                transform.Rotate(0f, 0f, speed * delta);
            }
        }
    }
}
