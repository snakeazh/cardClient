using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Resources;
using App.Talent;
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

        /// <summary>图标资源 key（未解锁同样从 1 级行兜底），空表示配置未填。</summary>
        public string IconKey;
    }

    /// <summary>
    /// 天赋弹窗：列表读 ITalentService（未解锁显示 ???），点击条目打开天赋详情；
    /// BuyBtn 显示抽天赋金币价（购买流程未接入，暂不响应点击）。
    /// </summary>
    public sealed class TalentPopupViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly NavigationViewModel _navigation;
        private readonly ITalentService _talent;
        private readonly List<TalentItem> _items = new List<TalentItem>();

        public TalentPopupViewModel(
            IUIManager ui,
            NavigationViewModel navigation,
            ITalentService talent,
            IResourceService resources)
        {
            _ui = ui;
            _navigation = navigation;
            _talent = talent;
            Resources = resources;
            CloseCommand = new RelayCommand(Dismiss);
            BuyCostText = new ObservableProperty<string>(ResolveBuyCost());
            RebuildItems();
        }

        public IReadOnlyList<TalentItem> Items => _items;

        public ObservableProperty<string> BuyCostText { get; }

        /// <summary>供 View 异步加载条目图标（同图鉴 VM 暴露 Resources 的模式）。</summary>
        public IResourceService Resources { get; }

        public IRelayCommand CloseCommand { get; }

        protected override Task OnOpen(object args)
        {
            RebuildItems();
            return Task.CompletedTask;
        }

        public async Task OpenDetail(TalentItem item)
        {
            if (item == null || !item.Snapshot.IsOwned)
            {
                return;
            }

            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(TalentDetailViewModel));
                var vm = (TalentDetailViewModel)_ui.Registry.CreateViewModel(registration);
                vm.Setup(item.Snapshot.TalentId);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
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
                    IconKey = config != null ? ResResourcePaths.TalentIcon(config.Icon) : null,
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

        private static string ResolveBuyCost()
        {
            return GameConst.IsLoaded ? GameConst.Instance.TalentChestNeedGold.ToString() : "0";
        }

        private void Dismiss()
        {
            _navigation.ShowHome();
        }
    }
}
