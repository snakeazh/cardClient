using System;
using System.Threading.Tasks;
using App.Game;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 局内资源栏。挂 Resource 层，与 MainResource 同层：打开时 navigator 会藏起局外栏。
    /// 只显示局内金币；backBtn 仅战斗 HUD 显示。
    /// </summary>
    public sealed class GameResourceViewModel : ViewModelBase
    {
        private readonly GameSession _session;
        private readonly ResourceSlot _gold;

        public GameResourceViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _gold = new ResourceSlot(ResourceKind.Gold);
            GoldText = _gold.Amount;
            ShowBackBtn = new ObservableProperty<bool>(true);
            BackCommand = new RelayCommand(() => { });
        }

        public ObservableProperty<string> GoldText { get; }

        /// <summary>仅战斗 HUD 显示返回；商城 / 购买 / 结算期间隐藏。</summary>
        public ObservableProperty<bool> ShowBackBtn { get; }

        public IRelayCommand BackCommand { get; private set; }

        public void BindBack(IRelayCommand backCommand)
        {
            if (backCommand != null)
            {
                BackCommand = backCommand;
            }
        }

        protected override Task OnOpen(object args)
        {
            _session.Changed -= OnSessionChanged;
            _session.Changed += OnSessionChanged;
            RefreshGold();
            ShowBackBtn.Value = true;
            return Task.CompletedTask;
        }

        protected override Task OnClose()
        {
            Unsubscribe();
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Unsubscribe();
        }

        public void SetIcon(Sprite goldIcon)
        {
            if (goldIcon != null)
            {
                _gold.Icon.Value = goldIcon;
            }
        }

        public ResourceSlot GoldSlot => _gold;

        private void OnSessionChanged()
        {
            RefreshGold();
        }

        private void RefreshGold()
        {
            _gold.Amount.Value = _session.Run.Gold.ToString();
        }

        private void Unsubscribe()
        {
            _session.Changed -= OnSessionChanged;
        }
    }
}
