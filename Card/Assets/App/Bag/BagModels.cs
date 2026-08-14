using System;

namespace App.Bag
{
    [Serializable]
    public sealed class BagEntry
    {
        public int ItemId;
        public int Count;

        public BagEntry()
        {
        }

        public BagEntry(int itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }
    }

    [Serializable]
    public sealed class BagSaveData
    {
        public BagEntry[] Entries = Array.Empty<BagEntry>();
    }
}
