using System;
using App.Atlas;
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
    /// <summary>
    /// 引导层：代码生成蒙版挖孔；中间提示窗用预制体上的 ChatBaseFrame（宽 945、最小高 406）。
    /// </summary>
    [AutoScreen(AppScreenIds.GuideOverlay, UILayer.Guide, ResResourcePaths.GuideOverlay)]
    public sealed class GuideOverlayView : ViewBase<GuideOverlayViewModel>
    {
        private const float MaskAlpha = 0.6f;
        /// <summary>局内卡牌（TargetId 以 Board. 开头）挖孔：宽高各 +40（每边 +20）。</summary>
        private const float BoardCardHoleExpandXy = 40f;
        /// <summary>UI 目标挖孔：宽高各 +10（每边 +5）。</summary>
        private const float UiHoleExpandXy = 10f;
        private const float ChatWidth = 945f;
        private const float ChatMinHeight = 406f;
        /// <summary>提示框与挖孔边缘的间距。</summary>
        private const float ChatHoleGap = 24f;
        private static readonly Color MaskTint = new Color(1f, 1f, 1f, MaskAlpha);

        private RectTransform _root;
        private RectTransform[] _bars;
        private RectTransform _holeFrame;
        private RectTransform _chatFrame;
        private TMP_Text _tipText;
        private GameObject _nextBtn;
        private GameObject _skipBtn;
        private GuideTargetRegistry _targets;
        private Rect _hole;
        private bool _hasHole;
        private bool _spritesReady;

        protected override void OnBind()
        {
            _root = (RectTransform)transform;
            EnsureUi();
            ApplyMaskSprites();
            _targets = AppServices.Resolve<GuideTargetRegistry>();
            Binding.BindText(_tipText, ViewModel.Text);
            Binding.BindActive(_chatFrame.gameObject, ViewModel.ShowBubble);
            Binding.BindActive(_nextBtn, ViewModel.ShowNext);
            Binding.BindActive(_skipBtn, ViewModel.ShowSkip);
            Binding.BindCommand(_nextBtn.GetComponent<Button>(), ViewModel.NextCommand);
            Binding.BindCommand(_skipBtn.GetComponent<Button>(), ViewModel.SkipCommand);
        }

        private void LateUpdate()
        {
            if (ViewModel == null)
            {
                return;
            }

            if (!_spritesReady)
            {
                ApplyMaskSprites();
            }

            UpdateHole();
            LayoutMask();
            LayoutChatFrame();
            TryDismissPeekTipClick();
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

            if (!_targets.TryGetOverlayHole(targetId, _root, out var hole, out var isUi))
            {
                return;
            }

            // 局内卡牌 / UI 分开扩边。
            float expandXy;
            if (IsBoardCardTarget(targetId))
            {
                expandXy = BoardCardHoleExpandXy;
            }
            else if (isUi)
            {
                expandXy = UiHoleExpandXy;
            }
            else
            {
                expandXy = 0f;
            }

            if (expandXy > 0f)
            {
                var half = expandXy * 0.5f;
                hole.xMin -= half;
                hole.yMin -= half;
                hole.xMax += half;
                hole.yMax += half;
            }

            _hole = hole;
            _hasHole = hole.width > 1f && hole.height > 1f;
        }

        private static bool IsBoardCardTarget(string targetId)
        {
            return !string.IsNullOrEmpty(targetId) &&
                   targetId.StartsWith("Board.", System.StringComparison.Ordinal);
        }

        private void LayoutMask()
        {
            var show = ViewModel.ShowMask.Value;
            for (var i = 0; i < _bars.Length; i++)
            {
                _bars[i].gameObject.SetActive(show);
            }

            if (_holeFrame != null)
            {
                _holeFrame.gameObject.SetActive(show && _hasHole);
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

            // 纯色遮罩：四边贴齐挖孔，不重叠（重叠处会叠出更深的线）。
            var xMin = Mathf.Clamp(_hole.xMin, r.xMin, r.xMax);
            var xMax = Mathf.Clamp(_hole.xMax, r.xMin, r.xMax);
            var yMin = Mathf.Clamp(_hole.yMin, r.yMin, r.yMax);
            var yMax = Mathf.Clamp(_hole.yMax, r.yMin, r.yMax);
            SetBar(_bars[0], r.xMin, xMin, r.yMin, r.yMax);
            SetBar(_bars[1], xMax, r.xMax, r.yMin, r.yMax);
            SetBar(_bars[2], xMin, xMax, yMax, r.yMax);
            SetBar(_bars[3], xMin, xMax, r.yMin, yMin);
            SetBar(_holeFrame, xMin, xMax, yMin, yMax);
        }

        private void LayoutChatFrame()
        {
            if (_chatFrame == null || !ViewModel.ShowBubble.Value)
            {
                return;
            }

            var height = ChatMinHeight;
            if (_tipText != null)
            {
                _tipText.ForceMeshUpdate();
                var tipH = _tipText.preferredHeight;
                height = Mathf.Max(ChatMinHeight, tipH + 120f);
            }

            _chatFrame.anchorMin = _chatFrame.anchorMax = new Vector2(0.5f, 0.5f);
            _chatFrame.pivot = new Vector2(0.5f, 0.5f);
            _chatFrame.sizeDelta = new Vector2(ChatWidth, height);

            var r = _root.rect;
            var halfW = ChatWidth * 0.5f;
            var halfH = height * 0.5f;
            const float edgePad = 16f;
            float x;
            float y;
            if (_hasHole)
            {
                // 紧贴挖孔：优先上方，放不下再下方；始终挨着挖孔，不要甩到屏幕对侧。
                x = Mathf.Clamp(_hole.center.x, r.xMin + halfW + edgePad, r.xMax - halfW - edgePad);
                var above = _hole.yMax + halfH + ChatHoleGap;
                var below = _hole.yMin - halfH - ChatHoleGap;
                if (above + halfH <= r.yMax - edgePad)
                {
                    y = above;
                }
                else if (below - halfH >= r.yMin + edgePad)
                {
                    y = below;
                }
                else
                {
                    // 上下都挤：夹在挖孔与屏幕边缘之间，仍靠近挖孔。
                    y = Mathf.Clamp(above, r.yMin + halfH + edgePad, r.yMax - halfH - edgePad);
                    if (ChatOverlapsHole(x, y, halfW, halfH))
                    {
                        y = Mathf.Clamp(below, r.yMin + halfH + edgePad, r.yMax - halfH - edgePad);
                    }
                }

                // 仍与挖孔重叠时挪到左右空档。
                if (ChatOverlapsHole(x, y, halfW, halfH))
                {
                    var leftX = _hole.xMin - halfW - ChatHoleGap;
                    var rightX = _hole.xMax + halfW + ChatHoleGap;
                    if (leftX - halfW >= r.xMin + edgePad)
                    {
                        x = leftX;
                    }
                    else if (rightX + halfW <= r.xMax - edgePad)
                    {
                        x = rightX;
                    }
                }
            }
            else
            {
                x = 0f;
                y = 0f;
            }

            _chatFrame.anchoredPosition = new Vector2(x, y);
            _chatFrame.SetAsLastSibling();
        }

        private bool ChatOverlapsHole(float x, float y, float halfW, float halfH)
        {
            var chat = Rect.MinMaxRect(x - halfW, y - halfH, x + halfW, y + halfH);
            return chat.Overlaps(_hole);
        }

        /// <summary>
        /// 「点击任意地方关闭」：引导层在 GameUI tip 之上，必须由 Overlay 收点击再关 tip，
        /// 否则 ChatBaseFrame / 遮罩会挡住 tip 的关闭热区，步骤卡住进不了搓牌引导。
        /// </summary>
        private void TryDismissPeekTipClick()
        {
            if (!Input.GetMouseButtonDown(0) || ViewModel == null)
            {
                return;
            }

            if (!string.Equals(
                    ViewModel.WaitHandler.Value,
                    GuideWaitIds.PeekGoodTipClosed,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var gameUi = FindObjectOfType<GameUIView>();
            gameUi?.DismissPeekGoodTipForGuide();
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
            if (bar == null)
            {
                return;
            }

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

            BindPrefabChat();

            _bars = new RectTransform[4];
            _bars[0] = CreateBar("MaskLeft");
            _bars[1] = CreateBar("MaskRight");
            _bars[2] = CreateBar("MaskTop");
            _bars[3] = CreateBar("MaskBottom");
            for (var i = 0; i < _bars.Length; i++)
            {
                _bars[i].SetAsFirstSibling();
            }

            _holeFrame = CreateStretch("HoleFrame");
            var holeImg = _holeFrame.gameObject.AddComponent<Image>();
            holeImg.color = MaskTint;
            holeImg.raycastTarget = false;
            holeImg.type = Image.Type.Sliced;
            _holeFrame.gameObject.SetActive(false);
            _holeFrame.SetSiblingIndex(_bars.Length);

            _nextBtn = CreateButton(_chatFrame, "NextBtn", "下一步", new Vector2(0f, -150f), new Vector2(180f, 44f));
            _skipBtn = CreateButton(_root, "SkipBtn", "跳过", new Vector2(420f, 860f), new Vector2(160f, 56f));
            var skipRt = (RectTransform)_skipBtn.transform;
            skipRt.anchorMin = skipRt.anchorMax = new Vector2(1f, 1f);
            skipRt.pivot = new Vector2(1f, 1f);
            skipRt.anchoredPosition = new Vector2(-28f, -28f);
        }

        private void BindPrefabChat()
        {
            var frameGo = FindChild(_root, "ChatBaseFrame");
            if (frameGo == null)
            {
                Debug.LogError("[GuideOverlay] 预制体缺少 ChatBaseFrame，请在 GuideOverlay 上放置对话框。");
                _chatFrame = CreateStretch("ChatBaseFrame");
                var bg = _chatFrame.gameObject.AddComponent<Image>();
                bg.raycastTarget = false;
                _tipText = CreateTmp(_chatFrame, string.Empty, 41f);
            }
            else
            {
                _chatFrame = (RectTransform)frameGo.transform;
                var tipGo = FindChild(_chatFrame, "tip");
                _tipText = tipGo != null ? tipGo.GetComponent<TMP_Text>() : null;
                if (_tipText == null)
                {
                    _tipText = _chatFrame.GetComponentInChildren<TMP_Text>(true);
                }
            }

            _chatFrame.sizeDelta = new Vector2(ChatWidth, ChatMinHeight);
            _chatFrame.anchoredPosition = Vector2.zero;
            _chatFrame.gameObject.SetActive(false);

            // 对话框不挡点击：挖孔/下层按钮（搓牌、商店货架）才能点到。
            DisableRaycastsExceptButtons(_chatFrame);
            if (_tipText != null)
            {
                _tipText.raycastTarget = false;
            }
        }

        private static void DisableRaycastsExceptButtons(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var graphics = root.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                if (g == null)
                {
                    continue;
                }

                // NextBtn 等按钮保留点击；框体/立绘/文案穿透，避免挡住挖孔与下层 UI。
                if (g.GetComponentInParent<Button>() != null)
                {
                    continue;
                }

                g.raycastTarget = false;
            }
        }

        private void ApplyMaskSprites()
        {
            if (_spritesReady || !AppServices.IsReady || _bars == null)
            {
                return;
            }

            var atlas = AppServices.Resolve<IAtlasService>();
            if (atlas == null || !atlas.IsLoaded)
            {
                return;
            }

            // 四块遮罩不用 BlackMask2：纯色铺满才能和挖孔紧贴，无图集边缘空隙。
            for (var i = 0; i < _bars.Length; i++)
            {
                var img = _bars[i].GetComponent<Image>();
                if (img == null)
                {
                    continue;
                }

                img.sprite = null;
                img.type = Image.Type.Simple;
                img.color = new Color(0f, 0f, 0f, MaskAlpha);
            }

            var mask = atlas.GetSprite(ResResourcePaths.GuideAtlas, "BlackMask");
            if (_holeFrame != null)
            {
                var holeImg = _holeFrame.GetComponent<Image>();
                if (holeImg != null && mask != null)
                {
                    holeImg.sprite = mask;
                    holeImg.type = Image.Type.Sliced;
                    holeImg.color = MaskTint;
                }
            }

            _spritesReady = true;
        }

        private RectTransform CreateBar(string name)
        {
            var rt = CreateStretch(name);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.color = new Color(0f, 0f, 0f, MaskAlpha);
            image.raycastTarget = true;
            image.type = Image.Type.Simple;
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
            image.color = new Color(0.85f, 0.55f, 0.2f, 1f);
            image.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            CreateTmp(rt, label, 28f);
            return go;
        }

        private static GameObject FindChild(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root.gameObject;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChild(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
