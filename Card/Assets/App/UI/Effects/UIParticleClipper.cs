using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 粒子视口裁剪：按参考矩形把世界坐标 _ClipRect/_UseClipRect 经 MaterialPropertyBlock
    /// 写进所有子 Renderer（shader LTY/FX/Simple 支持；矩形变化时才重写，滚动/适配自动跟随）。
    /// 矩形来源优先父链上的 RectMask2D，其次父链上 ScrollRect 自身矩形（ScrollRect 常与 Mask
    /// 同节点，取自身与图片裁剪区一致；不取 viewport——它常比可视区小，曾致特效提前被裁）。
    /// 都没有时不写入、特效保持不裁剪，可无脑挂。
    /// 注意不走 RectMask2D 的原生裁剪（那套只作用于 UGUI 图片，曾把视口内图片全部裁没）。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIParticleClipper : MonoBehaviour
    {
        private static readonly int ClipRectId = Shader.PropertyToID("_ClipRect");
        private static readonly int UseClipRectId = Shader.PropertyToID("_UseClipRect");

        private readonly Vector3[] _corners = new Vector3[4];
        private MaterialPropertyBlock _mpb;
        private RectTransform _clipSource;
        private Vector4 _lastRect;
        private bool _applied;

        private void OnEnable()
        {
            Apply(force: true);
        }

        private void OnTransformParentChanged()
        {
            _clipSource = null;
            _applied = false;
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

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
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
            var mask = GetComponentInParent<RectMask2D>();
            if (mask != null)
            {
                return mask.rectTransform;
            }

            var scroll = GetComponentInParent<ScrollRect>();
            return scroll != null ? (RectTransform)scroll.transform : null;
        }
    }
}
