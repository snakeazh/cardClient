using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 让整棵 UI 子树共用一条斜向流光。
    /// 每张 Image 使用独立材质实例，避免 Mask/Stencil 复制材质后进度写不进去。
    /// 频率由代码驱动，不依赖 Shader 的 _Time。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiFlowLight : MonoBehaviour
    {
        private const string ShaderName = "UI/FlowLight";
        private const float HiddenProgress = -1f;

        private static readonly int ShineColorId = Shader.PropertyToID("_ShineColor");
        private static readonly int ShineIntensityId = Shader.PropertyToID("_ShineIntensity");
        private static readonly int ShineWidthId = Shader.PropertyToID("_ShineWidth");
        private static readonly int ShineAngleId = Shader.PropertyToID("_ShineAngle");
        private static readonly int ShineProgressId = Shader.PropertyToID("_ShineProgress");
        private static readonly int RectId = Shader.PropertyToID("_FlowRect");

        private static Shader _cachedShader;

        [SerializeField] private Shader flowLightShader;
        [SerializeField] private bool autoPlay;
        [SerializeField] private float frequency = 0.4f;
        [SerializeField] [Range(0f, 4f)] private float intensity = 1.4f;
        [SerializeField] [Range(0.01f, 0.5f)] private float width = 0.12f;
        [SerializeField] [Range(-180f, 180f)] private float angle = 35f;
        [SerializeField] private Color shineColor = Color.white;

        private readonly List<Graphic> _graphics = new List<Graphic>(16);
        private readonly List<Material> _originals = new List<Material>(16);
        private readonly List<Material> _instances = new List<Material>(16);
        private readonly Vector3[] _corners = new Vector3[4];

        private Shader _shader;
        private bool _applied;
        private bool _playing;
        private float _elapsed;
        private float _progress = HiddenProgress;

        public float Frequency
        {
            get => frequency;
            set => frequency = Mathf.Max(0f, value);
        }

        public bool IsPlaying => _playing;

        [ContextMenu("Play Flow Light")]
        private void DebugPlay()
        {
            Play();
        }

        [ContextMenu("Stop Flow Light")]
        private void DebugStop()
        {
            Stop();
        }

        public void Play()
        {
            if (_playing)
            {
                return;
            }

            Apply();
            _playing = _applied;
        }

        public void Stop()
        {
            _playing = false;
            _elapsed = 0f;
            _progress = HiddenProgress;
            Restore();
        }

        public void SetFrequency(float hz)
        {
            Frequency = hz;
        }

        private void LateUpdate()
        {
            if (!_playing || !_applied)
            {
                return;
            }

            Advance();
            PushAll();
        }

        private void OnEnable()
        {
            if (autoPlay)
            {
                Play();
            }
        }

        private void OnDisable()
        {
            Stop();
        }

        private void OnDestroy()
        {
            Stop();
        }

        private void Advance()
        {
            if (frequency <= 0f)
            {
                _progress = HiddenProgress;
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            var cycle = 1f / frequency;
            _progress = Mathf.Repeat(_elapsed, cycle) / cycle;
        }

        private Shader ResolveShader()
        {
            if (_shader != null)
            {
                return _shader;
            }

            if (flowLightShader != null)
            {
                _shader = flowLightShader;
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
                    name = "UIFlowLight (Instance)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                mat.EnableKeyword("UNITY_UI_ALPHACLIP");
                mat.SetFloat("_UseUIAlphaClip", 1f);
                graphic.material = mat;
                _instances.Add(mat);
            }

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

            _graphics.Clear();
            _originals.Clear();
            DestroyInstances();
            _applied = false;
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
        }

        private void PushTo(Material mat, Vector4 rect)
        {
            if (mat == null)
            {
                return;
            }

            mat.SetColor(ShineColorId, shineColor);
            mat.SetFloat(ShineIntensityId, intensity);
            mat.SetFloat(ShineWidthId, width);
            mat.SetFloat(ShineAngleId, angle);
            mat.SetFloat(ShineProgressId, _progress);
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
            var rectWidth = Mathf.Max(0.0001f, max.x - min.x);
            var rectHeight = Mathf.Max(0.0001f, max.y - min.y);
            return new Vector4(min.x, min.y, rectWidth, rectHeight);
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
