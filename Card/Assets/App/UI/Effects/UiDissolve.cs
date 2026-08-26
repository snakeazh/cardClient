using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 让整棵 UI 子树按同一套世界坐标噪声溶解。
    /// 每张 Image 使用独立材质实例，避免 Mask/Stencil 复制材质后进度写不进去。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiDissolve : MonoBehaviour
    {
        private const string ShaderName = "UI/Dissolve";
        private const float HiddenAmount = 1.05f;

        private static readonly int AmountId = Shader.PropertyToID("_DissolveAmount");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int DirectionId = Shader.PropertyToID("_Direction");
        private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int RectId = Shader.PropertyToID("_DissolveRect");

        private static Shader _cachedShader;

        [SerializeField] private Shader dissolveShader;
        [SerializeField] private float duration = 0.65f;
        [SerializeField] private float noiseScale = 8f;
        [SerializeField] [Range(0f, 1f)] private float direction = 0.35f;
        [SerializeField] [Range(0.01f, 0.4f)] private float edgeWidth = 0.12f;
        [SerializeField] private Color edgeColor = new Color(1f, 0.55f, 0.12f, 1f);

        private readonly List<Graphic> _graphics = new List<Graphic>(16);
        private readonly List<Material> _originals = new List<Material>(16);
        private readonly List<Material> _instances = new List<Material>(16);
        private readonly List<TMP_Text> _texts = new List<TMP_Text>(8);
        private readonly List<float> _textAlphas = new List<float>(8);
        private readonly Vector3[] _corners = new Vector3[4];

        private Shader _shader;
        private Tween _tween;
        private CanvasGroup _fadeFallback;
        private Action _onComplete;
        private bool _applied;
        private float _amount;

        public float Amount => _amount;
        public bool IsPlaying => _tween != null && _tween.IsActive();
        public bool IsDissolved => _amount >= 1f && !IsPlaying;

        [ContextMenu("Play Dissolve")]
        private void DebugPlay()
        {
            Play();
        }

        [ContextMenu("Reset Dissolve")]
        private void DebugReset()
        {
            ResetState();
        }

        public Tween Play(float customDuration = -1f, Action onComplete = null)
        {
            if (onComplete != null)
            {
                _onComplete += onComplete;
            }

            if (IsPlaying)
            {
                return _tween;
            }

            if (IsDissolved)
            {
                InvokeComplete();
                return null;
            }

            var time = customDuration > 0f ? customDuration : duration;
            Apply();
            SetAmount(0f);

            _tween = DOTween.To(() => _amount, SetAmount, HiddenAmount, time)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .SetTarget(this)
                .OnComplete(InvokeComplete);
            return _tween;
        }

        public void ResetState()
        {
            KillTween();
            _onComplete = null;
            SetAmount(0f);
            Restore();
            if (_fadeFallback != null)
            {
                _fadeFallback.alpha = 1f;
            }
        }

        private void LateUpdate()
        {
            if (!_applied)
            {
                return;
            }

            PushAll();
        }

        private void OnDestroy()
        {
            KillTween();
            Restore();
        }

        private Shader ResolveShader()
        {
            if (_shader != null)
            {
                return _shader;
            }

            if (dissolveShader != null)
            {
                _shader = dissolveShader;
                return _shader;
            }

            if (_cachedShader == null)
            {
                _cachedShader = Shader.Find(ShaderName);
            }

            _shader = _cachedShader;
            return _shader;
        }

        private void Apply()
        {
            if (_applied)
            {
                PushAll();
                return;
            }

            var shader = ResolveShader();
            if (shader == null)
            {
                _fadeFallback = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
                _applied = true;
                return;
            }

            CollectGraphics();
            for (var i = 0; i < _graphics.Count; i++)
            {
                var graphic = _graphics[i];
                if (graphic == null)
                {
                    _instances.Add(null);
                    continue;
                }

                var mat = new Material(shader)
                {
                    name = "UIDissolve (Instance)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                mat.EnableKeyword("UNITY_UI_ALPHACLIP");
                mat.SetFloat("_UseUIAlphaClip", 1f);
                graphic.material = mat;
                _instances.Add(mat);
            }

            CollectTexts();
            _applied = true;
            PushAll();
        }

        private void CollectGraphics()
        {
            _graphics.Clear();
            _originals.Clear();
            DestroyInstances();
            var found = GetComponentsInChildren<MaskableGraphic>(true);
            for (var i = 0; i < found.Length; i++)
            {
                var graphic = found[i];
                if (graphic == null || graphic is TMP_Text || graphic is TMP_SubMeshUI)
                {
                    continue;
                }

                _graphics.Add(graphic);
                var current = graphic.material;
                _originals.Add(current != null && current != graphic.defaultMaterial ? current : null);
            }
        }

        private void CollectTexts()
        {
            _texts.Clear();
            _textAlphas.Clear();
            var found = GetComponentsInChildren<TMP_Text>(true);
            for (var i = 0; i < found.Length; i++)
            {
                var text = found[i];
                if (text == null)
                {
                    continue;
                }

                _texts.Add(text);
                _textAlphas.Add(text.alpha);
            }
        }

        private void Restore()
        {
            for (var i = 0; i < _graphics.Count; i++)
            {
                var graphic = _graphics[i];
                if (graphic != null)
                {
                    graphic.material = i < _originals.Count ? _originals[i] : null;
                }
            }

            for (var i = 0; i < _texts.Count; i++)
            {
                var text = _texts[i];
                if (text != null)
                {
                    text.alpha = _textAlphas[i];
                }
            }

            _graphics.Clear();
            _originals.Clear();
            _texts.Clear();
            _textAlphas.Clear();
            DestroyInstances();
            _applied = false;
        }

        private void SetAmount(float value)
        {
            _amount = value;
            PushAll();
            if (_fadeFallback != null && _instances.Count == 0)
            {
                _fadeFallback.alpha = 1f - Mathf.Clamp01(value);
            }
        }

        private void PushAll()
        {
            var rect = CurrentRect();
            for (var i = 0; i < _instances.Count; i++)
            {
                PushTo(_instances[i], rect);
                var graphic = i < _graphics.Count ? _graphics[i] : null;
                if (graphic != null)
                {
                    PushTo(graphic.materialForRendering, rect);
                }
            }

            var textHide = Mathf.Clamp01((_amount - 0.08f) / 0.35f);
            for (var i = 0; i < _texts.Count; i++)
            {
                var text = _texts[i];
                if (text != null)
                {
                    text.alpha = _textAlphas[i] * (1f - textHide);
                }
            }
        }

        private void PushTo(Material mat, Vector4 rect)
        {
            if (mat == null)
            {
                return;
            }

            mat.SetFloat(AmountId, _amount);
            mat.SetFloat(NoiseScaleId, noiseScale);
            mat.SetFloat(DirectionId, direction);
            mat.SetFloat(EdgeWidthId, edgeWidth);
            mat.SetColor(EdgeColorId, edgeColor);
            mat.SetVector(RectId, rect);
        }

        private Vector4 CurrentRect()
        {
            var rt = transform as RectTransform;
            if (rt == null)
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }

            rt.GetWorldCorners(_corners);
            var min = _corners[0];
            var max = _corners[2];
            var width = Mathf.Max(0.0001f, max.x - min.x);
            var height = Mathf.Max(0.0001f, max.y - min.y);
            return new Vector4(min.x, min.y, width, height);
        }

        private void InvokeComplete()
        {
            var done = _onComplete;
            _onComplete = null;
            _tween = null;
            done?.Invoke();
        }

        private void KillTween()
        {
            if (_tween != null && _tween.IsActive())
            {
                _tween.Kill();
            }

            _tween = null;
        }

        private void DestroyInstances()
        {
            for (var i = 0; i < _instances.Count; i++)
            {
                if (_instances[i] != null)
                {
                    Destroy(_instances[i]);
                }
            }

            _instances.Clear();
        }
    }
}
