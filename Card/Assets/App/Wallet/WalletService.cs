using System;
using Framework.Save;

namespace App.Wallet
{
    /// <summary>
    /// 局外货币，脏标记落盘。
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

        public void Add(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            _gold += amount;
            _dirty = true;
        }

        public void Load()
        {
            _gold = _save.GetInt(SaveKey, 0);
            if (_gold < 0)
            {
                _gold = 0;
            }

            _dirty = false;
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
