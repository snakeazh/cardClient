using System;
using Framework.Save;

namespace App.Wallet
{
    /// <summary>
    /// 局外货币，变更时立即落盘（微信小游戏上 pause/quit 不可靠，不能依赖统一 flush）。
    /// </summary>
    public sealed class WalletService : IWalletService
    {
        public const string SaveKey = "wallet.gold.v1";

        private readonly ISaveService _save;
        private int _gold;
        private bool _dirty;

        public WalletService(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public int Gold => _gold;

        public event Action Changed;

        public void Add(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            _gold += amount;
            _dirty = true;
            Save();
            Changed?.Invoke();
        }

        public void ReplaceFromServer(int gold)
        {
            var next = gold < 0 ? 0 : gold;
            if (_gold == next)
            {
                return;
            }

            _gold = next;
            _dirty = true;
            Changed?.Invoke();
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || _gold < amount)
            {
                return false;
            }

            // 花费 0（如首次抽天赋免费）视为成功，不置脏、不触发事件
            if (amount == 0)
            {
                return true;
            }

            _gold -= amount;
            _dirty = true;
            Save();
            Changed?.Invoke();
            return true;
        }

        public void Load()
        {
            _gold = _save.GetInt(SaveKey, 0);
            if (_gold < 0)
            {
                _gold = 0;
            }

            _dirty = false;
            Changed?.Invoke();
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            _save.SetInt(SaveKey, _gold);
            _save.Save();
            _dirty = false;
        }
    }
}
