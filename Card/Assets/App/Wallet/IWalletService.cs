using System;
using Framework.Save;

namespace App.Wallet
{
    /// <summary>
    /// 局外货币。闯关结算按积分 / GameConst.ExchangePointsForGoldCoins 兑入，落盘 wallet.gold.v1。
    /// </summary>
    public interface IWalletService : ISaveFlushable
    {
        int Gold { get; }

        event Action Changed;

        void Add(int amount);

        void Load();
    }
}
