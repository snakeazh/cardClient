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

        void Load();
    }
}
