using System;
using DG.Tweening;
using Framework.UI.Binding;
using Framework.UI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 数字滚动参数。
    /// </summary>
    public sealed class RollingTextOptions
    {
        /// <summary>单次滚动时长（秒）。</summary>
        public float Duration = 0.6f;

        /// <summary>滚动缓动。</summary>
        public Ease Ease = Ease.OutQuad;

        /// <summary>数值增加时是否叠加弹跳放大。</summary>
        public bool PopOnRoll = true;

        /// <summary>弹跳放大的倍数。</summary>
        public float PopScale = 1.2f;

        /// <summary>弹跳总时长（秒），放大与回弹各占一半。</summary>
        public float PopDuration = 0.3f;

        /// <summary>数值格式化；null 时直接 ToString()。例：v =&gt; v.ToString("N0") 为千分位。</summary>
        public Func<long, string> Formatter;
    }

    /// <summary>
    /// 通用数字滚动：把文本上的数字从当前显示值插值滚动到新值，增加时附带轻微弹跳。
    /// 常规接入用 RollingTextBindings.BindRollingText 替换 BindingContext.BindText 即可；
    /// 需手动驱动时直接 Set / RollTo。生命周期由 BindingContext 托管，视图关闭自动停止。
    /// </summary>
    public sealed class RollingText : IDisposable
    {
        private readonly UnityEngine.Object _target;
        private readonly Action<string> _setText;
        private readonly Transform _transform;
        private readonly Vector3 _baseScale;
        private readonly RollingTextOptions _options;

        private Tween _rollTween;
        private Tween _popTween;
        private long _current;
        private bool _hasValue;

        public long CurrentValue => _current;

        public RollingText(TMP_Text text, RollingTextOptions options = null)
            : this(text, text == null ? null : new Action<string>(value => text.text = value), options)
        {
        }

        public RollingText(Text text, RollingTextOptions options = null)
            : this(text, text == null ? null : new Action<string>(value => text.text = value), options)
        {
        }

        private RollingText(UnityEngine.Object target, Action<string> setText, RollingTextOptions options)
        {
            _target = target;
            _setText = setText;
            var component = target as Component;
            _transform = component != null ? component.transform : null;
            _baseScale = _transform != null ? _transform.localScale : Vector3.one;
            _options = options ?? new RollingTextOptions();
        }

        /// <summary>直接显示数值，不滚动（同时终止进行中的滚动）。</summary>
        public void Set(long value)
        {
            KillTweens();
            _hasValue = true;
            Apply(value);
        }

        /// <summary>直接显示原始文本（用于非数字内容），数值状态复位，下次数字重新起步。</summary>
        public void SetRaw(string raw)
        {
            KillTweens();
            _hasValue = false;
            if (_target == null)
            {
                return;
            }

            _setText(raw ?? string.Empty);
        }

        /// <summary>
        /// 滚动到目标值。首笔无起点时直接显示；滚动中目标再变化，会从当前显示值续滚到最新值。
        /// </summary>
        public void RollTo(long target)
        {
            if (!_hasValue)
            {
                Set(target);
                return;
            }

            if (target == _current)
            {
                return;
            }

            var increasing = target > _current;
            KillTweens();
            if (_options.Duration <= 0f)
            {
                Set(target);
            }
            else
            {
                var value = _current;
                _rollTween = DOTween.To(
                        () => value,
                        v =>
                        {
                            value = v;
                            Apply(v);
                        },
                        target,
                        _options.Duration)
                    .SetEase(_options.Ease)
                    .SetUpdate(true)
                    .SetTarget(this);
            }

            if (increasing && _options.PopOnRoll)
            {
                PlayPop();
            }
        }

        public void Dispose()
        {
            KillTweens();
            if (_transform != null)
            {
                _transform.localScale = _baseScale;
            }
        }

        private void Apply(long value)
        {
            if (_target == null)
            {
                // 文本已销毁：不再写值，tween 自然跑完或随绑定释放终止。
                return;
            }

            _current = value;
            _setText(_options.Formatter != null ? _options.Formatter(value) : value.ToString());
        }

        private void PlayPop()
        {
            if (_transform == null)
            {
                return;
            }

            _transform.localScale = _baseScale;
            var half = Mathf.Max(0.02f, _options.PopDuration * 0.5f);
            _popTween = DOTween.Sequence()
                .Append(_transform.DOScale(_baseScale * _options.PopScale, half).SetEase(Ease.OutQuad))
                .Append(_transform.DOScale(_baseScale, half).SetEase(Ease.InQuad))
                .SetUpdate(true)
                .SetTarget(this);
        }

        private void KillTweens()
        {
            if (_rollTween != null)
            {
                _rollTween.Kill();
                _rollTween = null;
            }

            if (_popTween != null)
            {
                _popTween.Kill();
                _popTween = null;
            }
        }
    }

    /// <summary>
    /// 数字滚动绑定：源属性变化时，文本数字从当前显示值滚动到新值。
    /// 字符串源按整数解析，解析失败原样直接显示；首笔直接设值不滚动。
    /// </summary>
    public static class RollingTextBindings
    {
        public static void BindRollingText(
            this BindingContext binding,
            TMP_Text text,
            ObservableProperty<string> source,
            RollingTextOptions options = null)
        {
            if (binding == null || text == null || source == null)
            {
                return;
            }

            BindRollingTextCore(binding, source, new RollingText(text, options));
        }

        public static void BindRollingText(
            this BindingContext binding,
            Text text,
            ObservableProperty<string> source,
            RollingTextOptions options = null)
        {
            if (binding == null || text == null || source == null)
            {
                return;
            }

            BindRollingTextCore(binding, source, new RollingText(text, options));
        }

        public static void BindRollingText(
            this BindingContext binding,
            TMP_Text text,
            ObservableProperty<int> source,
            RollingTextOptions options = null)
        {
            if (binding == null || text == null || source == null)
            {
                return;
            }

            var roller = new RollingText(text, options);
            binding.Add(roller);
            binding.Add(source.Subscribe(value => roller.RollTo(value)));
        }

        private static void BindRollingTextCore(BindingContext binding, ObservableProperty<string> source, RollingText roller)
        {
            binding.Add(roller);
            binding.Add(source.Subscribe(value =>
            {
                if (long.TryParse(value, out var number))
                {
                    roller.RollTo(number);
                }
                else
                {
                    roller.SetRaw(value);
                }
            }));
        }
    }
}
