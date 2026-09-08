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
        private GamePhase _lastPhase;
        private int _heldGold;

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
            _lastPhase = _session.Phase;
            if (_lastPhase == GamePhase.Shop)
            {
                _heldGold = Math.Max(0, _session.ShopGoldGranted);
            }

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

        /// <summary>结算飞币未到位前暂扣的金币，资源栏先显示扣除后的数量。</summary>
        public int HeldGold => _heldGold;

        public void HoldGold(int amount, bool refresh = true)
        {
            if (amount <= 0)
            {
                return;
            }

            _heldGold += amount;
            if (refresh)
            {
                RefreshGold();
            }
        }

        /// <summary>飞币到位后释放暂扣；amount ≤ 0 时全部释放。</summary>
        public void ReleaseHeldGold(int amount = 0)
        {
            if (_heldGold <= 0)
            {
                return;
            }

            if (amount <= 0 || amount >= _heldGold)
            {
                _heldGold = 0;
            }
            else
            {
                _heldGold -= amount;
            }

            RefreshGold();
        }

        private void OnSessionChanged()
        {
            var phase = _session.Phase;
            if (phase == GamePhase.Shop && _lastPhase != GamePhase.Shop)
            {
                _heldGold = Math.Max(0, _session.ShopGoldGranted);
            }
            else if (phase != GamePhase.Shop)
            {
                _heldGold = 0;
            }

            _lastPhase = phase;
            RefreshGold();
        }

        private void RefreshGold()
        {
            var gold = _session.Run != null ? _session.Run.Gold : 0;
            gold -= _heldGold;
            if (gold < 0)
            {
                gold = 0;
            }

            _gold.Amount.Value = gold.ToString();
        }

        private void Unsubscribe()
        {
            _session.Changed -= OnSessionChanged;
        }
    }
}
