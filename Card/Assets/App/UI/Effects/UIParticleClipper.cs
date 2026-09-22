using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 粒子视口裁剪：按参考矩形把世界坐标 _ClipRect/_UseClipRect 经 MaterialPropertyBlock
    /// 写进所有子 Renderer（shader LTY/FX/Simple 支持；矩形变化时才重写，滚动/适配自动跟随）。
    /// 矩形来源优先父链上 ScrollRect 自身矩形（不取 Viewport——它常比可视区矮，且 Viewport
    /// 常挂 RectMask2D，若先查 Mask 会退回 Viewport 矩形，曾致特效提前被裁）；
    /// 无 ScrollRect 时再回退 RectMask2D。都没有时不写入、特效保持不裁剪，可无脑挂。
    /// 注意不走 RectMask2D 的原生裁剪（那套只作用于 UGUI 图片，曾把视口内图片全部裁没）。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIParticleClipper : MonoBehaviour
    {
        private static readonly int ClipRectId = Shader.PropertyToID("_ClipRect");
        private static readonly int UseClipRectId = Shader.PropertyToID("_UseClipRect");

        private readonly Vector3[] _corners = new Vector3[4];
        // 注:App.UI 下有同名子命名空间 App.UI.List,这里必须全限定
        private readonly System.Collections.Generic.List<Renderer> _renderers =
            new System.Collections.Generic.List<Renderer>(8);
        private bool _renderersStale = true;
        private MaterialPropertyBlock _mpb;
        private RectTransform _clipSource;
        private Vector4 _lastRect;
        private bool _applied;

        private void OnEnable()
        {
            _renderersStale = true;
            Apply(force: true);
        }

        private void OnTransformParentChanged()
        {
            _clipSource = null;
            _applied = false;
            _renderersStale = true;
            enabled = true;
            Apply(force: true);
        }

        private void LateUpdate()
        {
            Apply(force: false);
        }

        private void Apply(bool force)
        {
            if (_clipSource == null)
            {
                _clipSource = ResolveClipSource();
                _applied = false;
            }

            if (_clipSource == null)
            {
                // 父链上没有可参考的矩形：不裁剪也无需每帧轮询，等换父再试
                enabled = false;
                return;
            }

            _clipSource.GetWorldCorners(_corners);
            var rect = new Vector4(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y);
            if (!force && _applied && rect == _lastRect)
            {
                return;
            }

            _lastRect = rect;
            _applied = true;
            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }

            // 子树 Renderer 缓存:滚动期矩形每帧变,GetComponentsInChildren 的数组分配是热点;
            // 仅 force(启用/换父)时重查。运行时动态增删特效子节点的话需再触发 force。
            if (force || _renderersStale)
            {
                _renderers.Clear();
                GetComponentsInChildren(true, _renderers);
                _renderersStale = false;
            }

            for (var i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_mpb);
                _mpb.SetFloat(UseClipRectId, 1f);
                _mpb.SetVector(ClipRectId, rect);
                renderer.SetPropertyBlock(_mpb);
            }
        }

        private RectTransform ResolveClipSource()
        {
            // ScrollRect 自身矩形优先：Viewport 常挂 RectMask2D，先查 Mask 会拿到 Viewport
            // 矩形（比可视区矮，特效会被提前裁掉）；仅无 ScrollRect 时才用 RectMask2D。
            var scroll = GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                return (RectTransform)scroll.transform;
            }

            var mask = GetComponentInParent<RectMask2D>();
            return mask != null ? mask.rectTransform : null;
        }
    }
}
