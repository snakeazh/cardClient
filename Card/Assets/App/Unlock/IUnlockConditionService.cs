using System.Collections.Generic;
using Framework.Save;
using App.Config;
using CardShare.Contracts.Config;

namespace App.Unlock
{
    /// <summary>
    /// 遗物解锁：账号累计 <see cref="UnlockConditionConfig"/> 进度，商店用开局快照。
    /// </summary>
    public interface IUnlockConditionService : ISaveFlushable
    {
        bool IsRelicUnlocked(int relicId);

        /// <summary>条件当前累计值，没有进度为 0。</summary>
        int GetProgress(int conditionId);

        /// <summary>本局商店货池：开局已解锁或无条件。局内新解锁要等下次闯关。</summary>
        bool IsRelicInShopPool(int relicId);

        void BeginRun();

        /// <summary>
        /// 取出局内积压的新解锁遗物 Id（按解锁顺序）。回主页后调用以弹 GetEquipDetail；取出后清空。
        /// </summary>
        IReadOnlyList<int> ConsumePendingUnlockRelicIds();

        /// <summary>
        /// 上报进度。<paramref name="amount"/> 默认 1。
        /// 累加类：进度 += amount × StackedValue；取最大类（通关难度 / 持有金币 / 单次伤害）：进度 = max(当前, amount)。
        /// </summary>
        void Report(ContidionType type, int amount = 1);

        /// <summary>用服务器主档覆盖解锁进度。仅 <c>ApplyServerProfile</c> 调用。</summary>
        void ReplaceFromServer(IReadOnlyList<UnlockConditionProgressEntry> progress);

        void Load();
    }
}
