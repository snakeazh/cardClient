using System;
using Framework.Save;

namespace App.PvpScore
{
    /// <summary>
    /// 局外 PVP 分数。权威在服务器主档 <c>PvpScore</c>，本地仅缓存展示。
    /// </summary>
    public interface IPvpScoreService : ISaveFlushable
    {
        int Score { get; }

        event Action Changed;

        /// <summary>用服务器主档覆盖本地分数。仅 <c>ApplyServerProfile</c> 调用。</summary>
        void ReplaceFromServer(int score);

        void Load();
    }
}
