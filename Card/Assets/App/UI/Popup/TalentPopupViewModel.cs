using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Atlas;
using App.Config;
using App.Resources;
using App.Talent;
using App.Wallet;
using Framework.Assets;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 列表条目：ITalentService 快照 + 配置名（未解锁时快照无 Config，名字从 1 级行兜底）。
    /// </summary>
    public sealed class TalentItem
    {
        public TalentSnapshot Snapshot;
        public string Name;

        /// <summary>Altas/Talent 图集内 sprite 名（未解锁同样从 1 级行兜底），空表示配置未填。</summary>
        public string IconKey;

        /// <summary>品质（取自当前行，未解锁按 1 级行兜底），View 据此分区。</summary>
        public QualityType Type;
    }

    /// <summary>
    /// 天赋弹窗：列表读 ITalentService（未解锁显示 ???），点击条目打开天赋详情；
    /// BuyBtn 按 GetDrawCost（基础价 + 已抽次数 × 递增步长）扣金币，从非满级天赋中随机抽一个，
    /// 弹详情展示结果，金币不足时 Toast 提示，全部满级时 Toast 提示。
    /// </summary>
    public sealed class TalentPopupViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly ITalentService _talent;
        private readonly IWalletService _wallet;
        private readonly List<TalentItem> _items = new List<TalentItem>();

        public TalentPopupViewModel(
            IUIManager ui,
            ITalentService talent,
            IWalletService wallet,
            IResourceService resources,
            IAtlasService atlas)
        {
            _ui = ui;
            _talent = talent;
            _wallet = wallet;
            Resources = resources;
            Atlas = atlas;
            BuyCommand = new RelayCommand(Buy);
            OpenRulesCommand = new RelayCommand(OpenRules);
            BuyCostText = new ObservableProperty<string>(_talent.GetDrawCost().ToString());
            ListVersion = new ObservableProperty<int>(0);
            RebuildItems();
        }

        public IReadOnlyList<TalentItem> Items => _items;

        public ObservableProperty<string> BuyCostText { get; }

        /// <summary>购买后自增，View 订阅后重建列表（新抽到的天赋解锁显示）。</summary>
        public ObservableProperty<int> ListVersion { get; }

        /// <summary>供 View 异步加载条目图标（同图鉴 VM 暴露 Resources 的模式）。</summary>
        public IResourceService Resources { get; }

        /// <summary>取 Altas/Talent 天赋图标 sprite（TalentConfig.Icon 为 sprite 名）。</summary>
        public IAtlasService Atlas { get; }

        public IRelayCommand BuyCommand { get; }

        /// <summary>打开天赋规则说明弹窗（DetailBtn）。</summary>
        public IRelayCommand OpenRulesCommand { get; }

        protected override Task OnOpen(object args)
        {
            RebuildItems();
            return Task.CompletedTask;
        }

        public Task OpenDetail(TalentItem item)
        {
            return item == null ? Task.CompletedTask : OpenDetail(item.Snapshot.TalentId);
        }

        /// <param name="reward">抽卡获得入口：隐藏左右切换、显示恭喜获得图。</param>
        public async Task OpenDetail(int talentId, bool reward = false)
        {
            if (_talent.GetLevel(talentId) <= 0)
            {
                return;
            }

            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(TalentDetailViewModel));
                var vm = (TalentDetailViewModel)_ui.Registry.CreateViewModel(registration);
                vm.Setup(talentId, reward);
                // 详情页看广告升级后刷新列表（等级角标、解锁染色）
                vm.Upgraded += OnDetailUpgraded;
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
        }

        private void OnDetailUpgraded(int talentId)
        {
            RebuildItems();
            ListVersion.Value++;
        }

        /// <summary>打开天赋规则说明弹窗（DetailBtn，GameConst.TalentDesc 文案）。</summary>
        private async void OpenRules()
        {
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(TalentRulesPopViewModel));
                var vm = (TalentRulesPopViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
        }

        private void Buy()
        {
            if (!GameConst.IsLoaded)
            {
                return;
            }

            var cost = _talent.GetDrawCost();
            var talentId = _talent.DrawRandomId();
            if (talentId <= 0)
            {
                Toast.Show("天赋已全部满级");
                return;
            }

            if (!_wallet.TrySpend(cost))
            {
                Toast.Show("金币不足");
                return;
            }

            _talent.Add(talentId);
            _talent.RecordDraw();
            BuyCostText.Value = _talent.GetDrawCost().ToString();
            RebuildItems();
            ListVersion.Value++;
            _ = OpenDetail(talentId, reward: true);
        }

        private void RebuildItems()
        {
            _items.Clear();
            var ids = _talent.GetIds();
            for (var i = 0; i < ids.Count; i++)
            {
                var snapshot = _talent.GetCurrent(ids[i]);
                var config = ResolveConfig(snapshot);
                _items.Add(new TalentItem
                {
                    Snapshot = snapshot,
                    Name = config != null ? config.Name : null,
                    IconKey = config != null && !string.IsNullOrWhiteSpace(config.Icon)
                        ? config.Icon.Trim()
                        : null,
                    Type = config != null ? config.Type : QualityType.Ordinary,
                });
            }
        }

        /// <summary>当前级行；未解锁时快照无 Config，回退 1 级行取名字/图标。</summary>
        private TalentConfig ResolveConfig(TalentSnapshot snapshot)
        {
            if (snapshot.Config != null)
            {
                return snapshot.Config;
            }

            return _talent.TryGet(snapshot.TalentId, 1, out var row) ? row : null;
        }
    }
}
