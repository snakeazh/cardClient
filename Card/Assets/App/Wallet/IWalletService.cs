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

        /// <summary>用服务器主档覆盖本地金币。仅 <c>ApplyServerProfile</c> 调用。</summary>
        void ReplaceFromServer(int gold);

        /// <summary>扣款。余额不足或金额为负返回 false 且不改动余额；花费 0 视为成功。</summary>
        bool TrySpend(int amount);

        void Load();
    }
}
