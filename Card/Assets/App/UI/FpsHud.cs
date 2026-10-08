using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 帧耗时 HUD（左上角常驻，纯代码构建无 prefab 依赖）：每 0.5s 刷新一次最近窗口的
    /// 实际 FPS / 平均帧耗 / 最大单帧耗 / 超时帧数（&gt;16.7ms）。
    /// 用于定位「帧率不掉但一顿一顿」的单帧尖峰（微信工具只能看 FPS 曲线，拿不到单帧耗时）。
    /// 文案用纯 ASCII，避免 LeMi 子集字体缺字。
    /// 开关：Editor 走 Debug/性能 菜单（写 PlayerPrefs 持久）；启动时 AppBootstrap 按 PrefKey 自动恢复。
    /// </summary>
    public sealed class FpsHud : MonoBehaviour
    {
        public const string PrefKey = "debug_fps_hud";

        private const float RefreshInterval = 0.5f;
        private const float BudgetMs = 16.7f; // 60fps 预算；改 GameFps 后按需调整

        private static FpsHud _instance;

        private TMP_Text _text;
        private float _windowElapsed;
        private int _windowFrames;
        private float _windowMaxMs;
        private int _windowOver;
        private float _refreshTimer;
        private long _lastGcMemory = -1;
        private float _windowGcAllocMb;
        private int _lastGcCount = -1;
        private int _windowGcCount;

        public static bool IsShown => _instance != null;

        public static void Show()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("FpsHud");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FpsHud>();
            _instance.Build();
        }

        public static void Hide()
        {
            if (_instance == null)
            {
                return;
            }

            Destroy(_instance.gameObject);
            _instance = null;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0f;

            var bg = new GameObject("bg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(transform, false);
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = new Vector2(0f, 1f);
            bgRt.anchorMax = new Vector2(0f, 1f);
            bgRt.pivot = new Vector2(0f, 1f);
            bgRt.anchoredPosition = new Vector2(8f, -40f);
            bgRt.sizeDelta = new Vector2(640f, 250f);
            bg.GetComponent<Image>().raycastTarget = false;
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var textGo = new GameObject("text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(bgRt, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(14f, 10f);
            textRt.offsetMax = new Vector2(-14f, -10f);
            _text = textGo.GetComponent<TextMeshProUGUI>();
            _text.fontSize = 38f;
            _text.color = new Color(0.5f, 1f, 0.5f);
            _text.enableWordWrapping = false;
            _text.raycastTarget = false;
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _windowElapsed += dt;
            _windowFrames++;
            var ms = dt * 1000f;
            if (ms > _windowMaxMs)
            {
                _windowMaxMs = ms;
            }

            if (ms > BudgetMs)
            {
                _windowOver++;
            }

            // GC 观测：窗口内托管堆增量（负增量=已回收，钳 0 累计）与 Gen0 收集次数。
            // 滑动时 alloc/collect 激增即可锁定 GC 尖峰（安卓 WASM 的 GC 为全停顿）。
            var gcMemory = GC.GetTotalMemory(false);
            if (_lastGcMemory >= 0)
            {
                var delta = gcMemory - _lastGcMemory;
                if (delta > 0)
                {
                    _windowGcAllocMb += delta / (1024f * 1024f);
                }
            }

            _lastGcMemory = gcMemory;
            var gcCount = GC.CollectionCount(0);
            if (_lastGcCount >= 0)
            {
                _windowGcCount += gcCount - _lastGcCount;
            }

            _lastGcCount = gcCount;
            _refreshTimer += dt;
            if (_refreshTimer < RefreshInterval)
            {
                return;
            }

            _refreshTimer = 0f;
            var fps = _windowElapsed > 0f ? _windowFrames / _windowElapsed : 0f;
            var avg = _windowFrames > 0 ? _windowElapsed * 1000f / _windowFrames : 0f;
            var sb = new StringBuilder(128);
            sb.Append("FPS ").Append(fps.ToString("F1"));
            sb.Append("  avg ").Append(avg.ToString("F1")).Append("ms\n");
            sb.Append("max ").Append(_windowMaxMs.ToString("F1")).Append("ms");
            sb.Append("  late ").Append(_windowOver).Append("\n");
            sb.Append("GC ").Append(_windowGcAllocMb.ToString("F2")).Append("MB");
            sb.Append("  gen0 ").Append(_windowGcCount);
            _text.text = sb.ToString();
            _windowElapsed = 0f;
            _windowFrames = 0;
            _windowMaxMs = 0f;
            _windowOver = 0;
            _windowGcAllocMb = 0f;
            _windowGcCount = 0;
        }
    }
}
