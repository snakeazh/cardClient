using System.Collections.Generic;
using App.Atlas;
using App.Config;
using CardShare.Contracts.Config;
using App.Resources;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Popup
{
    /// <summary>
    /// 局内新解锁遗物详情：回主页后弹出，上下切换多件解锁，点 Mask / 确定关闭。
    /// CongratulationsImg 常开；AcquireMethod 显示解锁条件描述；equipNum 为 (当前/总数)。
    /// </summary>
    public sealed class GetEquipDetailViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly List<int> _relicIds = new List<int>();
        private int _index;

        public GetEquipDetailViewModel(IUIManager ui, IAtlasService atlas)
        {
            _ui = ui;
            Atlas = atlas;
            NameText = new ObservableProperty<string>();
            DescText = new ObservableProperty<string>();
            AcquireMethodText = new ObservableProperty<string>(string.Empty);
            EquipNumText = new ObservableProperty<string>(string.Empty);
            IconSprite = new ObservableProperty<Sprite>();
            Quality = new ObservableProperty<QualityType>(QualityType.Ordinary);
            ShowSwitch = new ObservableProperty<bool>(false);
            ShowAcquireMethod = new ObservableProperty<bool>(false);
            PrevCommand = new RelayCommand(() => Shift(-1));
            NextCommand = new RelayCommand(() => Shift(1));
            CloseCommand = new RelayCommand(Dismiss);
        }

        public IAtlasService Atlas { get; }

        public ObservableProperty<string> NameText { get; }

        public ObservableProperty<string> DescText { get; }

        public ObservableProperty<string> AcquireMethodText { get; }

        /// <summary>获取个数文案，如「(1/3)」；仅一件时仍显示 (1/1)。</summary>
        public ObservableProperty<string> EquipNumText { get; }

        public ObservableProperty<Sprite> IconSprite { get; }

        public ObservableProperty<QualityType> Quality { get; }

        public ObservableProperty<bool> ShowSwitch { get; }

        public ObservableProperty<bool> ShowAcquireMethod { get; }

        public IRelayCommand PrevCommand { get; }

        public IRelayCommand NextCommand { get; }

        public IRelayCommand CloseCommand { get; }

        /// <summary>按解锁顺序填入遗物 Id；空列表时打开后立即关闭。</summary>
        public void Setup(IReadOnlyList<int> relicIds)
        {
            _relicIds.Clear();
            if (relicIds != null)
            {
                for (var i = 0; i < relicIds.Count; i++)
                {
                    var id = relicIds[i];
                    if (id > 0 && RelicConfig.Get(id) != null)
                    {
                        _relicIds.Add(id);
                    }
                }
            }

            _index = 0;
            Apply();
        }

        private void Shift(int delta)
        {
            if (_relicIds.Count <= 1)
            {
                return;
            }

            _index = (_index + delta + _relicIds.Count) % _relicIds.Count;
            Apply();
        }

        private void Apply()
        {
            if (_relicIds.Count == 0)
            {
                NameText.Value = string.Empty;
                DescText.Value = string.Empty;
                AcquireMethodText.Value = string.Empty;
                EquipNumText.Value = string.Empty;
                IconSprite.Value = null;
                Quality.Value = QualityType.Ordinary;
                ShowSwitch.Value = false;
                ShowAcquireMethod.Value = false;
                return;
            }

            var relic = RelicConfig.Get(_relicIds[_index]);
            NameText.Value = relic != null ? relic.Name ?? string.Empty : string.Empty;
            DescText.Value = relic != null ? relic.Desc ?? string.Empty : string.Empty;
            Quality.Value = relic != null ? relic.Type : QualityType.Ordinary;
            IconSprite.Value = ResolveIcon(relic);
            EquipNumText.Value = $"({_index + 1}/{_relicIds.Count})";
            ShowSwitch.Value = _relicIds.Count > 1;

            var acquire = ResolveAcquireMethod(relic);
            ShowAcquireMethod.Value = !string.IsNullOrEmpty(acquire);
            AcquireMethodText.Value = acquire ?? string.Empty;
        }

        private Sprite ResolveIcon(RelicConfig relic)
        {
            if (Atlas == null || relic == null || string.IsNullOrWhiteSpace(relic.Icon))
            {
                return null;
            }

            Atlas.TryGetSprite(ResResourcePaths.RelicAtlas, relic.Icon.Trim(), out var sprite);
            return sprite;
        }

        private static string ResolveAcquireMethod(RelicConfig relic)
        {
            if (relic == null)
            {
                return null;
            }

            var desc = relic.UnlockConditionId > 0
                ? UnlockConditionConfig.Get(relic.UnlockConditionId)?.Desc
                : null;
            if (string.IsNullOrEmpty(desc))
            {
                desc = "商店购买获得";
            }

            return "获取方式: \n" + desc;
        }

        private void Dismiss()
        {
            _ = _ui.Close(this);
        }
    }
}
