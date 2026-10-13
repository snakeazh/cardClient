using System.Collections.Generic;
using Framework.Save;

namespace App.Bag
{
    /// <summary>
    /// Bag of ItemConfig rows keyed by item Id with stack counts.
    /// </summary>
    public interface IBagService : ISaveFlushable
    {
        bool IsDirty { get; }

        int GetCount(int itemId);

        bool Has(int itemId, int amount = 1);

        void Add(int itemId, int amount);

        bool TryRemove(int itemId, int amount);

        IReadOnlyList<BagEntry> GetAll();

        void Clear();

        /// <summary>用服务器主档覆盖背包。仅 <c>ApplyServerProfile</c> 调用。</summary>
        void ReplaceFromServer(IReadOnlyList<BagEntry> entries);

        void Load();
    }
}
