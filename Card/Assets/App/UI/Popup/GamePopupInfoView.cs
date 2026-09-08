using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Resources;
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
    /// 文案为「名称：描述」。closeBtn 点空白关闭。
    /// </summary>
    [AutoScreen(AppScreenIds.GamePopupInfo, UILayer.Popup, ResResourcePaths.GamePopupInfo)]
    public sealed class GamePopupInfoView : ViewBase<GamePopupInfoViewModel>
    {
        private readonly List<GameObject> _rows = new List<GameObject>();
        private GameObject _template;

        protected override void OnBind()
        {
            var closeBtn = GetNode<Button>("closeBtn");
            if (closeBtn != null)
            {
                Binding.BindCommand(closeBtn, ViewModel.CloseCommand);
            }

            EnsureTemplate();
            Binding.Add(ViewModel.ListVersion.Subscribe(_ => RefreshRows(), emitCurrent: true));
        }

        protected override Task OnViewClose()
        {
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
                var text = clone.GetComponentInChildren<TMP_Text>(true);
                if (text != null)
                {
                    text.text = FormatEntry(entry);
                }

                _rows.Add(clone);
            }
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
