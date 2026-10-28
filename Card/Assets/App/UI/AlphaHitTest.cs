using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 透明像素点击穿透：点击落在 Sprite alpha 达到阈值的像素上才判定命中，
    /// 透明区域不响应也不拦截（点击会落到下方的 UI 上）。
    /// 挂在带 Image 的节点上（通常是按钮根节点），Button/Toggle 不用替换，onClick 与过渡全部保留。
    /// 默认对子节点所有 Image 一并生效：本工程按钮常是“本体图 + 底框 + 文字图”多张叠在子节点，
    /// 子图 raycastTarget 未关时，最终命中区域 = 各图不透明像素的并集（与视觉一致）。
    /// 前提：贴图须开启 Read/Write 且不使用 Crunch 压缩，否则 UGUI 读不到像素会静默退回整矩形命中——
    /// 散图在导入设置勾选 Read/Write Enabled；打进图集的图须在 SpriteAtlas 的平台设置开启。
    /// 漏配时只在编辑器 / Development 包打一条 warning（每个贴图一次），正式包静默退回整矩形。
    /// 注意 Read/Write 会额外保留一份贴图 CPU 内存，只给真正需要异形点击的图开。
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class AlphaHitTest : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly HashSet<int> LoggedUnreadable = new HashSet<int>();
#endif

        [Tooltip("像素 alpha 达到该阈值才算命中；0 = 关闭（整矩形可点）")]
        [SerializeField, Range(0f, 1f)]
        private float alphaThreshold = 0.5f;

        [Tooltip("对子节点的 Image 一并生效（命中区域 = 各图不透明像素的并集）")]
        [SerializeField]
        private bool applyToChildren = true;

        private Image[] _targets;

        private void Awake()
        {
            _targets = CollectTargets();
            Apply();
        }

        private void OnValidate()
        {
            // Play 模式下拖滑条可即时生效
            if (Application.isPlaying)
            {
                _targets = _targets ?? CollectTargets();
                Apply();
            }
        }

        private Image[] CollectTargets()
        {
            return applyToChildren
                ? GetComponentsInChildren<Image>(true)
                : new[] { GetComponent<Image>() };
        }

        private static bool IsCrunchFormat(TextureFormat format)
        {
            return format == TextureFormat.DXT1Crunched
                || format == TextureFormat.DXT5Crunched
                || format == TextureFormat.ETC_RGB4Crunched
                || format == TextureFormat.ETC2_RGBA8Crunched;
        }

        private void Apply()
        {
            foreach (var image in _targets)
            {
                if (image == null)
                {
                    continue;
                }

                var sprite = image.sprite;
                if (sprite == null)
                {
                    image.alphaHitTestMinimumThreshold = alphaThreshold; // 无 Sprite 时 UGUI 视为整矩形命中
                    continue;
                }

                // uGUI 的 setter 在贴图不可读或 Crunch 压缩时对任何赋值（包括 0）都抛异常，此时完全不赋值
                var tex = sprite.texture;
                if (tex == null || !tex.isReadable || IsCrunchFormat(tex.format))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (tex != null && !tex.isReadable && LoggedUnreadable.Add(tex.GetInstanceID()))
                    {
                        Debug.LogWarning(
                            $"[AlphaHitTest] 贴图 {tex.name} 未开启 Read/Write，alpha 命中不生效，已退回整矩形可点。" +
                            "散图请在贴图导入设置勾选 Read/Write Enabled；图集请在 SpriteAtlas 平台设置开启。", image);
                    }
#endif
                    continue;
                }

                image.alphaHitTestMinimumThreshold = alphaThreshold;
            }
        }
    }
}
