using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Resources;
using DG.Tweening;
using Framework.Log;
using Framework.UI.Binding;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 关卡机制说明弹窗。stageinfo 为行模板，按本关 BossEntryConfig 克隆，
    /// 文案为「名称：描述」。每条从右侧依次划入，点空白关闭。
    /// </summary>
    [AutoScreen(AppScreenIds.GamePopupInfo, UILayer.Popup, ResResourcePaths.GamePopupInfo)]
    public sealed class GamePopupInfoView : ViewBase<GamePopupInfoViewModel>
    {
        private const float RowSlideDuration = 0.36f;
        private const float RowSlideStagger = 0.16f;
        private const float RowSlideExtra = 80f;

        private readonly List<GameObject> _rows = new List<GameObject>();
        private GameObject _template;
        private LayoutGroup _parentLayout;
        private ContentSizeFitter _parentFitter;
        private Sequence _slideSeq;

        protected override void OnBind()
        {
            var closeBtn = GetNode<Button>("closeBtn");
            if (closeBtn != null)
            {
                Binding.BindCommand(closeBtn, ViewModel.CloseCommand);
            }

            EnsureTemplate();
            // OnBind 先于 VM.OnOpen；emitCurrent:false 避免版本 0 时空列表先播一次入场。
            Binding.Add(ViewModel.ListVersion.Subscribe(_ => RefreshRows(), emitCurrent: false));
        }

        protected override Task OnViewClose()
        {
            KillSlide();
            RestoreParentLayout();
            ClearRows();
            return Task.CompletedTask;
        }

        /// <summary>UIBind 条目存的组件类型不保证（如 closeBtn 存的是 CanvasRenderer），统一走 GameObject 再取组件。</summary>
        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }

        private void EnsureTemplate()
        {
            var go = UI.GetGameObject("stageinfo");
            if (go == null)
            {
                AppLog.Warn(LogChannel.UI, "GamePopupInfo 缺少 stageinfo 模板", this);
                return;
            }

            _template = go;
            _template.SetActive(false);
        }

        private void RefreshRows()
        {
            if (_template == null || ViewModel == null)
            {
                return;
            }

            KillSlide();
            RestoreParentLayout();
            ClearRows();
            var entries = ViewModel.ResolveEntries();
            var parent = _template.transform.parent;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                var clone = Instantiate(_template, parent, false);
                clone.name = $"stageinfo{i + 1}";
                var binds = clone.GetComponentsInChildren<UIBind>(true);
                for (var b = 0; b < binds.Length; b++)
                {
                    Destroy(binds[b]);
                }

                clone.SetActive(true);
                var group = GetOrAddCanvasGroup(clone);
                group.alpha = 0f;
                var text = clone.GetComponentInChildren<TMP_Text>(true);
                if (text != null)
                {
                    text.text = FormatEntry(entry);
                }

                _rows.Add(clone);
            }

            PlayRowSlideIn();
        }

        private void PlayRowSlideIn()
        {
            if (_rows.Count == 0)
            {
                return;
            }

            var parent = _template.transform.parent as RectTransform;
            if (parent != null)
            {
                for (var i = 0; i < _rows.Count; i++)
                {
                    var rowRt = _rows[i].transform as RectTransform;
                    if (rowRt != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(rowRt);
                    }
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
                FreezeParentLayout(parent);
            }

            _slideSeq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            for (var i = 0; i < _rows.Count; i++)
            {
                var rt = _rows[i].transform as RectTransform;
                if (rt == null)
                {
                    continue;
                }

                var home = rt.anchoredPosition;
                var width = Mathf.Max(rt.rect.width, 160f) + RowSlideExtra;
                rt.anchoredPosition = home + new Vector2(width, 0f);

                var group = GetOrAddCanvasGroup(_rows[i]);
                group.alpha = 0f;
                var at = i * RowSlideStagger;
                _slideSeq.Insert(at, rt.DOAnchorPos(home, RowSlideDuration)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true)
                    .SetLink(rt.gameObject, LinkBehaviour.KillOnDestroy));
                _slideSeq.Insert(at, group.DOFade(1f, RowSlideDuration * 0.65f)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .SetLink(group.gameObject, LinkBehaviour.KillOnDestroy));
            }

            _slideSeq.OnComplete(RestoreParentLayout);
        }

        private void FreezeParentLayout(RectTransform parent)
        {
            _parentLayout = parent.GetComponent<LayoutGroup>();
            _parentFitter = parent.GetComponent<ContentSizeFitter>();
            if (_parentLayout != null)
            {
                _parentLayout.enabled = false;
            }

            if (_parentFitter != null)
            {
                _parentFitter.enabled = false;
            }
        }

        private void RestoreParentLayout()
        {
            if (_parentLayout != null)
            {
                _parentLayout.enabled = true;
            }

            if (_parentFitter != null)
            {
                _parentFitter.enabled = true;
            }
        }

        private void KillSlide()
        {
            if (_slideSeq != null && _slideSeq.IsActive())
            {
                _slideSeq.Kill();
            }

            _slideSeq = null;
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            return group != null ? group : go.AddComponent<CanvasGroup>();
        }

        private static string FormatEntry(BossEntryConfig entry)
        {
            var name = entry.Name ?? string.Empty;
            var desc = entry.Desc ?? string.Empty;
            if (string.IsNullOrEmpty(desc))
            {
                return name;
            }

            if (string.IsNullOrEmpty(name))
            {
                return desc;
            }

            return $"{name}：{desc}";
        }

        private void ClearRows()
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    Destroy(_rows[i]);
                }
            }

            _rows.Clear();
        }
    }
}
