using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 用不规则竖缝把 UI 切成左右两半，再向两侧掉落。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiSplit : MonoBehaviour
    {
        private const string ShaderName = "UI/Split";
        private const float GapOpen = 0.02f;
        private const float CrackPortion = 0.18f;

        private static readonly int SplitSideId = Shader.PropertyToID("_SplitSide");
        private static readonly int CrackAmplitudeId = Shader.PropertyToID("_CrackAmplitude");
        private static readonly int CrackScaleId = Shader.PropertyToID("_CrackScale");
        private static readonly int CrackSeedId = Shader.PropertyToID("_CrackSeed");
        private static readonly int GapId = Shader.PropertyToID("_Gap");
        private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int RectId = Shader.PropertyToID("_SplitRect");
        private static readonly int ClipRectId = Shader.PropertyToID("_ClipRect");
        private static readonly Vector4 OpenClipRect = new Vector4(-32767f, -32767f, 32767f, 32767f);

        private static Shader _cachedShader;

        [SerializeField] private Shader splitShader;
        [SerializeField] private float duration = 0.55f;
        [SerializeField] private float startDelay = 1f;
        [SerializeField] [Range(0f, 0.5f)] private float crackAmplitude = 0.18f;
        [SerializeField] private float crackScale = 6f;
        [SerializeField] [Range(0.001f, 0.2f)] private float edgeWidth = 0.035f;
        [SerializeField] private Color edgeColor = new Color(1f, 0.95f, 0.8f, 0f);
        [SerializeField] private Vector2 fallOffset = new Vector2(80f, -100f);
        [SerializeField] private float rotateAngle = 12f;

        private readonly Half _left = new Half { Side = -1f };
        private readonly Half _right = new Half { Side = 1f };
        private readonly Vector3[] _corners = new Vector3[4];

        private Shader _shader;
        private Tween _tween;
        private readonly List<Behaviour> _hiddenGraphics = new List<Behaviour>(16);
        private Action _onComplete;
        private bool _splitDone;
        private bool _visualClone;
        private int _playToken;
        private float _progress;
        private float _gap;
        private float _seed;

        public bool IsPlaying => _tween != null && _tween.IsActive();
        public bool IsSplit => _splitDone && !IsPlaying;

        [ContextMenu("Play Split")]
        private void DebugPlay()
        {
            Play();
        }

        [ContextMenu("Reset Split")]
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

            if (IsSplit)
            {
                InvokeComplete();
                return null;
            }

            var time = customDuration > 0f ? customDuration : duration;
            if (!Begin(time))
            {
                InvokeComplete();
                return null;
            }

            return _tween;
        }

        public void ResetState()
        {
            KillSeq();
            _onComplete = null;
            _splitDone = false;
            CleanupClones();
            RestoreOriginal();
        }

        private void OnDestroy()
        {
            if (_visualClone)
            {
                return;
            }

            KillSeq();
            CleanupClones();
        }

        private bool Begin(float time)
        {
            var shader = ResolveShader();
            if (shader == null)
            {
                return false;
            }

            var leftGo = CreateClone(" (SplitLeft)");
            var rightGo = CreateClone(" (SplitRight)");
            if (leftGo == null || rightGo == null)
            {
                DestroyClone(leftGo);
                DestroyClone(rightGo);
                return false;
            }

            leftGo.SetActive(true);
            rightGo.SetActive(true);
            BindHalf(_left, leftGo, shader);
            BindHalf(_right, rightGo, shader);
            if (_left.Root == null || _right.Root == null)
            {
                CleanupClones();
                return false;
            }

            Canvas.ForceUpdateCanvases();
            RecenterPivot(_left);
            RecenterPivot(_right);

            _seed = UnityEngine.Random.Range(0f, 64f);
            _gap = 0f;
            _progress = 0f;
            SetProgress(0f);
            HideOriginal();

            var token = ++_playToken;
            _tween = DOTween.To(() => _progress, SetProgress, 1f, Mathf.Max(0.05f, time))
                .SetDelay(Mathf.Max(0f, startDelay))
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .SetTarget(this)
                .OnComplete(() =>
                {
                    if (token != _playToken)
                    {
                        return;
                    }

                    Finish();
                });
            return true;
        }

        private void SetProgress(float value)
        {
            _progress = Mathf.Clamp01(value);
            if (_progress <= CrackPortion)
            {
                var t = CrackPortion <= 0f ? 1f : _progress / CrackPortion;
                _gap = GapOpen * t;
                Pose(_left, 0f);
                Pose(_right, 0f);
            }
            else
            {
                _gap = GapOpen;
                var u = (1f - CrackPortion) <= 0f ? 1f : (_progress - CrackPortion) / (1f - CrackPortion);
                Pose(_left, u);
                Pose(_right, u);
            }

            PushAll();
        }

        private void Pose(Half half, float ease)
        {
            if (half.Root == null)
            {
                return;
            }

            var side = half.Side < 0f ? -1f : 1f;
            half.Root.anchoredPosition = half.HomePos + new Vector2(side * fallOffset.x, fallOffset.y) * ease;
            half.Root.localEulerAngles = half.HomeEuler + new Vector3(0f, 0f, -side * rotateAngle) * ease;
        }

        private GameObject CreateClone(string suffix)
        {
            if (transform.parent == null)
            {
                return null;
            }

            var clone = Instantiate(gameObject, transform.parent, false);
            clone.name = name + suffix;
            if (clone.TryGetComponent(out UiSplit split))
            {
                split._visualClone = true;
            }

        
            clone.SetActive(false);
            var rt = clone.transform as RectTransform;
            if (rt != null)
            {
                rt.SetSiblingIndex(transform.GetSiblingIndex() + 1);
            }

            StripLogic(clone);
            HideTexts(clone);
            return clone;
        }

        private void BindHalf(Half half, GameObject clone, Shader shader)
        {
            half.Root = clone.transform as RectTransform;
            if (clone.TryGetComponent(out CanvasGroup leftover))
            {
                leftover.alpha = 1f;
                DestroyImmediate(leftover);
            }

            half.HomePos = half.Root != null ? half.Root.anchoredPosition : Vector2.zero;
            half.HomeEuler = half.Root != null ? half.Root.localEulerAngles : Vector3.zero;
            half.Graphics.Clear();
            DestroyMaterials(half);

            var found = clone.GetComponentsInChildren<MaskableGraphic>(true);
            for (var i = 0; i < found.Length; i++)
            {
                var graphic = found[i];
                if (graphic == null || graphic is TMP_Text || graphic is TMP_SubMeshUI)
                {
                    continue;
                }

                graphic.enabled = true;
                graphic.canvasRenderer.cull = false;

                var mat = new Material(shader)
                {
                    name = "UISplit (Instance)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                mat.SetColor("_Color", Color.white);
                mat.SetVector(ClipRectId, OpenClipRect);
                graphic.material = mat;
                graphic.SetAllDirty();
                half.Graphics.Add(graphic);
                half.Instances.Add(mat);
            }
        }

        private static void StripLogic(GameObject clone)
        {
            var behaviours = clone.GetComponentsInChildren<Behaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null || Keep(behaviour))
                {
                    continue;
                }

                if (behaviour is Animator animator)
                {
                    animator.keepAnimatorStateOnDisable = true;
                }

                behaviour.enabled = false;
                DestroyImmediate(behaviour);
            }
        }

        private static bool Keep(Behaviour behaviour)
        {
            return behaviour is Graphic
                || behaviour is Mask
                || behaviour is RectMask2D
                || behaviour is CanvasGroup
                || behaviour is BaseMeshEffect
                || behaviour is LayoutElement;
        }

        private static void HideTexts(GameObject clone)
        {
            var texts = clone.GetComponentsInChildren<TMP_Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null)
                {
                    continue;
                }

                texts[i].alpha = 0f;
                texts[i].enabled = false;
            }
        }

        private void HideOriginal()
        {
            RestoreOriginal();
            var graphics = GetComponentsInChildren<MaskableGraphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (graphic == null || !graphic.enabled)
                {
                    continue;
                }

                graphic.enabled = false;
                _hiddenGraphics.Add(graphic);
            }
        }

        private void RestoreOriginal()
        {
            for (var i = 0; i < _hiddenGraphics.Count; i++)
            {
                if (_hiddenGraphics[i] != null)
                {
                    _hiddenGraphics[i].enabled = true;
                }
            }

            _hiddenGraphics.Clear();
        }

        private void Finish()
        {
            _tween = null;
            _splitDone = true;
            InvokeComplete();
        }

        private void CleanupClones()
        {
            _gap = 0f;
            _progress = 0f;
            DestroyHalf(_left);
            DestroyHalf(_right);
        }

        private static void DestroyHalf(Half half)
        {
            DestroyMaterials(half);
            half.Graphics.Clear();
            if (half.Root != null)
            {
                Destroy(half.Root.gameObject);
                half.Root = null;
            }
        }

        private static void DestroyClone(GameObject clone)
        {
            if (clone != null)
            {
                Destroy(clone);
            }
        }

        private static void DestroyMaterials(Half half)
        {
            for (var i = 0; i < half.Instances.Count; i++)
            {
                if (half.Instances[i] != null)
                {
                    Destroy(half.Instances[i]);
                }
            }

            half.Instances.Clear();
        }

        private void PushAll()
        {
            PushHalf(_left);
            PushHalf(_right);
        }

        private void PushHalf(Half half)
        {
            if (half.Root == null)
            {
                return;
            }

            var rect = GraphicsWorldRect(half);
            for (var i = 0; i < half.Instances.Count; i++)
            {
                PushTo(half.Instances[i], half.Side, rect);
                var graphic = i < half.Graphics.Count ? half.Graphics[i] : null;
                if (graphic != null)
                {
                    PushTo(graphic.materialForRendering, half.Side, rect);
                }
            }
        }

        private void PushTo(Material mat, float side, Vector4 rect)
        {
            if (mat == null)
            {
                return;
            }

            mat.SetFloat(SplitSideId, side);
            mat.SetFloat(CrackAmplitudeId, crackAmplitude);
            mat.SetFloat(CrackScaleId, crackScale);
            mat.SetFloat(CrackSeedId, _seed);
            mat.SetFloat(GapId, _gap);
            mat.SetFloat(EdgeWidthId, edgeWidth);
            mat.SetColor(EdgeColorId, edgeColor);
            mat.SetVector(RectId, rect);
        }

        private void RecenterPivot(Half half)
        {
            if (half.Root == null)
            {
                return;
            }

            var bounds = GraphicsWorldRect(half);
            var worldCenter = new Vector3(bounds.x + bounds.z * 0.5f, bounds.y + bounds.w * 0.5f, half.Root.position.z);
            var local = (Vector2)half.Root.InverseTransformPoint(worldCenter);
            var r = half.Root.rect;
            var newPivot = new Vector2(
                (local.x - r.xMin) / Mathf.Max(0.0001f, r.width),
                (local.y - r.yMin) / Mathf.Max(0.0001f, r.height));
            var delta = newPivot - half.Root.pivot;
            half.Root.pivot = newPivot;
            half.Root.anchoredPosition += Vector2.Scale(delta, r.size);
            half.HomePos = half.Root.anchoredPosition;
            half.HomeEuler = half.Root.localEulerAngles;
        }

        private Vector4 GraphicsWorldRect(Half half)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var any = false;
            for (var i = 0; i < half.Graphics.Count; i++)
            {
                var graphic = half.Graphics[i];
                if (graphic == null)
                {
                    continue;
                }

                graphic.rectTransform.GetWorldCorners(_corners);
                for (var c = 0; c < 4; c++)
                {
                    min = Vector2.Min(min, _corners[c]);
                    max = Vector2.Max(max, _corners[c]);
                    any = true;
                }
            }

            if (!any)
            {
                return WorldRect(half.Root);
            }

            return new Vector4(min.x, min.y, Mathf.Max(0.0001f, max.x - min.x), Mathf.Max(0.0001f, max.y - min.y));
        }

        private Vector4 WorldRect(RectTransform rt)
        {
            rt.GetWorldCorners(_corners);
            var min = Vector2.Min(Vector2.Min(_corners[0], _corners[1]), Vector2.Min(_corners[2], _corners[3]));
            var max = Vector2.Max(Vector2.Max(_corners[0], _corners[1]), Vector2.Max(_corners[2], _corners[3]));
            return new Vector4(
                min.x,
                min.y,
                Mathf.Max(0.0001f, max.x - min.x),
                Mathf.Max(0.0001f, max.y - min.y));
        }

        private Shader ResolveShader()
        {
            if (_shader != null)
            {
                return _shader;
            }

            if (splitShader != null)
            {
                _shader = splitShader;
                return _shader;
            }

            if (_cachedShader == null)
            {
                _cachedShader = Shader.Find(ShaderName);
            }

            _shader = _cachedShader;
            return _shader;
        }

        private void InvokeComplete()
        {
            var done = _onComplete;
            _onComplete = null;
            _tween = null;
            done?.Invoke();
        }

        private void KillSeq()
        {
            _playToken++;
            if (_tween != null && _tween.IsActive())
            {
                _tween.Kill();
            }

            _tween = null;
        }

        private sealed class Half
        {
            public float Side;
            public RectTransform Root;
            public Vector2 HomePos;
            public Vector3 HomeEuler;
            public readonly List<Graphic> Graphics = new List<Graphic>(16);
            public readonly List<Material> Instances = new List<Material>(16);
        }
    }
}
