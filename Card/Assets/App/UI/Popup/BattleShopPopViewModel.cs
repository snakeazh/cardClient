using System;
using System.Threading.Tasks;
using App.Atlas;
using App.Config;
using App.Game;
using App.Resources;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Popup
{
    /// <summary>
    /// 通关商店：sellHor 货架、MineHor 已购，点击打开 ShopDetail 购买或出售。
    /// </summary>
    public sealed class BattleShopPopViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly IDialogService _dialogs;
        private ShopDetailViewModel _detail;

        public BattleShopPopViewModel(
            GameSession session,
            IUIManager ui,
            IDialogService dialogs,
            IAtlasService atlas)
        {
            Session = session;
            Atlas = atlas;
            _ui = ui;
            _dialogs = dialogs;
            RefreshGoldNum = new ObservableProperty<string>(session.EffectiveShopRefreshCost.ToString());
            GoldText = new ObservableProperty<string>(session.Run.Gold.ToString());
            ShopRevision = new ObservableProperty<int>();
            RefreshCommand = new RelayCommand(() => Session.RefreshShopOffers(), () => Session.CanRefreshShop);
            NextStageCommand = new RelayCommand(Leave);
            CloseCommand = new RelayCommand(Leave);
        }

        public GameSession Session { get; }

        public IAtlasService Atlas { get; }

        public ObservableProperty<string> RefreshGoldNum { get; }

        public ObservableProperty<string> GoldText { get; }

        public ObservableProperty<int> ShopRevision { get; }

        public IRelayCommand RefreshCommand { get; }

        public IRelayCommand NextStageCommand { get; }

        public IRelayCommand CloseCommand { get; }

        public Sprite GetRelicIcon(RelicConfig relic)
        {
            if (relic == null || string.IsNullOrWhiteSpace(relic.Icon) || Atlas == null)
            {
                return null;
            }

            Atlas.TryGetSprite(ResResourcePaths.RelicAtlas, relic.Icon.Trim(), out var sprite);
            return sprite;
        }

        public async Task OpenDetail(int relicId, bool buying)
        {
            if (relicId <= 0)
            {
                return;
            }

            if (buying && !Session.Run.ShopOfferIds.Contains(relicId))
            {
                return;
            }

            if (!buying && !Session.OwnsRelicConfig(relicId))
            {
                return;
            }

            try
            {
                if (_detail != null)
                {
                    await _ui.Close(_detail);
                    _detail = null;
                }

                var registration = _ui.Registry.GetByViewModelType(typeof(ShopDetailViewModel));
                var vm = (ShopDetailViewModel)_ui.Registry.CreateViewModel(registration);
                vm.Setup(relicId, buying);
                _detail = vm;
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _detail = null;
            }
        }

        protected override Task OnOpen(object args)
        {
            Session.Changed -= OnSessionChanged;
            Session.Changed += OnSessionChanged;
            Refresh();
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Session.Changed -= OnSessionChanged;
            if (_detail != null)
            {
                var detail = _detail;
                _detail = null;
                _ = _ui.Close(detail);
            }
        }

        private void OnSessionChanged()
        {
            Refresh();
        }

        private void Refresh()
        {
            RefreshGoldNum.Value = Session.EffectiveShopRefreshCost.ToString();
            GoldText.Value = Session.Run.Gold.ToString();
            RefreshCommand.RaiseCanExecuteChanged();
            ShopRevision.Value++;
        }

        private void Leave()
        {
            if (_detail != null)
            {
                var detail = _detail;
                _detail = null;
                _ = _ui.Close(detail);
            }

            Session.LeaveShop();
            _ = _dialogs.CloseWithResult(true);
        }
    }
}
