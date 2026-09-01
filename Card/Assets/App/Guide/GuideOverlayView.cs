using App.Bootstrap;
using App.Resources;
using App.UI;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Guide
{
    /// <summary>TopMost 之上的引导层：四块打洞蒙版 + 手指 + 气泡。</summary>
    [AutoScreen(AppScreenIds.GuideOverlay, UILayer.Guide, ResResourcePaths.GuideOverlay)]
    public sealed class GuideOverlayView : ViewBase<GuideOverlayViewModel>
    {
        private static readonly Color MaskColor = new Color(0f, 0f, 0f, 0.65f);

        private RectTransform _root;
        private RectTransform[] _bars;
        private RectTransform _finger;
        private RectTransform _bubble;
        private TMP_Text _bubbleText;
        private GameObject _nextBtn;
        private GameObject _skipBtn;
        private GuideTargetRegistry _targets;
        private Rect _hole;
        private bool _hasHole;

        protected override void OnBind()
        {
            _root = (RectTransform)transform;
            EnsureUi();
            _targets = AppServices.Resolve<GuideTargetRegistry>();
            Binding.BindText(_bubbleText, ViewModel.Text);
            Binding.BindActive(_bubble.gameObject, ViewModel.ShowBubble);
            Binding.BindActive(_nextBtn, ViewModel.ShowNext);
            Binding.BindActive(_skipBtn, ViewModel.ShowSkip);
            Binding.BindActive(_finger.gameObject, ViewModel.ShowFinger);
            Binding.BindCommand(_nextBtn.GetComponent<Button>(), ViewModel.NextCommand);
            Binding.BindCommand(_skipBtn.GetComponent<Button>(), ViewModel.SkipCommand);
        }

        private void LateUpdate()
        {
            if (ViewModel == null)
            {
                return;
            }

            UpdateHole();
            LayoutMask();
            LayoutFinger();
            LayoutBubble();
            TryHoleClick();
        }

        private void UpdateHole()
        {
            _hasHole = false;
            var targetId = ViewModel.TargetId.Value;
            if (string.IsNullOrEmpty(targetId) || _targets == null)
            {
                return;
            }

            if (!_targets.TryGetOverlayHole(targetId, _root, out var hole))
            {
                return;
            }

            var pad = ViewModel.Padding.Value;
            if (pad > 0)
            {
                hole.xMin -= pad;
                hole.yMin -= pad;
                hole.xMax += pad;
                hole.yMax += pad;
            }

            _hole = hole;
            _hasHole = hole.width > 1f && hole.height > 1f;
        }

        private void LayoutMask()
        {
            var show = ViewModel.ShowMask.Value;
            for (var i = 0; i < _bars.Length; i++)
            {
                _bars[i].gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            var r = _root.rect;
            if (!_hasHole)
            {
                SetBar(_bars[0], r.xMin, r.xMax, r.yMin, r.yMax);
                SetBar(_bars[1], 0f, 0f, 0f, 0f);
                SetBar(_bars[2], 0f, 0f, 0f, 0f);
                SetBar(_bars[3], 0f, 0f, 0f, 0f);
                return;
            }

            var xMin = Mathf.Clamp(_hole.xMin, r.xMin, r.xMax);
            var xMax = Mathf.Clamp(_hole.xMax, r.xMin, r.xMax);
            var yMin = Mathf.Clamp(_hole.yMin, r.yMin, r.yMax);
            var yMax = Mathf.Clamp(_hole.yMax, r.yMin, r.yMax);
            SetBar(_bars[0], r.xMin, xMin, r.yMin, r.yMax);
            SetBar(_bars[1], xMax, r.xMax, r.yMin, r.yMax);
            SetBar(_bars[2], xMin, xMax, yMax, r.yMax);
            SetBar(_bars[3], xMin, xMax, r.yMin, yMin);
        }

        private void LayoutFinger()
        {
            if (!ViewModel.ShowFinger.Value || !_hasHole)
            {
                return;
            }

            var bounce = Mathf.PingPong(Time.unscaledTime * 40f, 18f);
            _finger.anchoredPosition = new Vector2(_hole.center.x, _hole.yMin - 36f + bounce);
        }

        private void LayoutBubble()
        {
            if (!ViewModel.ShowBubble.Value)
            {
                return;
            }

            var r = _root.rect;
            var size = _bubble.sizeDelta;
            var y = _hasHole ? _hole.yMax + size.y * 0.5f + 28f : 120f;
            if (_hasHole && y + size.y * 0.5f > r.yMax - 20f)
            {
                y = _hole.yMin - size.y * 0.5f - 48f;
            }

            var x = _hasHole ? _hole.center.x : 0f;
            var half = size.x * 0.5f;
            x = Mathf.Clamp(x, r.xMin + half + 16f, r.xMax - half - 16f);
            _bubble.anchoredPosition = new Vector2(x, y);
        }

        private void TryHoleClick()
        {
            if (!Input.GetMouseButtonDown(0) || !_hasHole)
            {
                return;
            }

            if (!ViewModel.HoleClickCommand.CanExecute())
            {
                return;
            }

            var canvas = _root.GetComponentInParent<Canvas>();
            var cam = canvas != null
                ? (canvas.worldCamera != null
                    ? canvas.worldCamera
                    : canvas.rootCanvas != null ? canvas.rootCanvas.worldCamera : null)
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _root, Input.mousePosition, cam, out var local))
            {
                return;
            }

            if (_hole.Contains(local))
            {
                ViewModel.HoleClickCommand.Execute();
            }
        }

        private static void SetBar(RectTransform bar, float xMin, float xMax, float yMin, float yMax)
        {
            var w = Mathf.Max(0f, xMax - xMin);
            var h = Mathf.Max(0f, yMax - yMin);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
            bar.sizeDelta = new Vector2(w, h);
        }

        private void EnsureUi()
        {
            if (_bars != null)
            {
                return;
            }

            _bars = new RectTransform[4];
            _bars[0] = CreateBar("MaskLeft");
            _bars[1] = CreateBar("MaskRight");
            _bars[2] = CreateBar("MaskTop");
            _bars[3] = CreateBar("MaskBottom");

            _finger = CreateStretch("Finger");
            _finger.sizeDelta = new Vector2(72f, 72f);
            var fingerText = CreateTmp(_finger, "▼", 48f);
            fingerText.color = Color.white;

            _bubble = CreateStretch("Bubble");
            _bubble.sizeDelta = new Vector2(560f, 160f);
            var bubbleBg = _bubble.gameObject.AddComponent<Image>();
            bubbleBg.color = new Color(0.12f, 0.12f, 0.16f, 0.94f);
            bubbleBg.raycastTarget = false;
            _bubbleText = CreateTmp(_bubble, string.Empty, 30f);
            var textRt = (RectTransform)_bubbleText.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(24f, 52f);
            textRt.offsetMax = new Vector2(-24f, -20f);
            _bubbleText.alignment = TextAlignmentOptions.Center;

            _nextBtn = CreateButton(_bubble, "NextBtn", "下一步", new Vector2(0f, -52f), new Vector2(180f, 44f));
            _skipBtn = CreateButton(_root, "SkipBtn", "跳过", new Vector2(420f, 860f), new Vector2(160f, 56f));
            var skipRt = (RectTransform)_skipBtn.transform;
            skipRt.anchorMin = skipRt.anchorMax = new Vector2(1f, 1f);
            skipRt.pivot = new Vector2(1f, 1f);
            skipRt.anchoredPosition = new Vector2(-28f, -28f);
        }

        private RectTransform CreateBar(string name)
        {
            var rt = CreateStretch(name);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = MaskColor;
            image.raycastTarget = true;
            return rt;
        }

        private RectTransform CreateStretch(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private static TMP_Text CreateTmp(RectTransform parent, string text, float size)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null)
            {
                tmp.font = TMP_Settings.defaultFontAsset;
            }

            return tmp;
        }

        private static GameObject CreateButton(RectTransform parent, string name, string label, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.2f, 0.45f, 0.85f, 1f);
            image.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            CreateTmp(rt, label, 28f);
            return go;
        }
    }
}
