using System.Collections.Generic;
using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 发牌堆/座位卡牌（CardIcon prefab）对象池。每局发牌要铺一整副 52 张堆牌再发座位牌，
    /// 不池化就是每局 52 次 Instantiate + 约 50 次 Destroy，小游戏单线程下直接掉帧。
    /// 回收时停用并复位动画/特效（<see cref="CardItem.ResetForPool"/>），
    /// 显示状态（牌面/朝向/透明度）由租用方的 CardItem.Initialize 全量重置。
    /// </summary>
    public static class CardItemPool
    {
        private static Transform _root;
        private static readonly Stack<CardItem> _items = new Stack<CardItem>();

        public static CardItem Rent(GameObject prefab)
        {
            // Unity 假空：池根节点随场景销毁等情况，弹出已销毁引用直接跳过。
            while (_items.Count > 0)
            {
                var pooled = _items.Pop();
                if (pooled != null)
                {
                    pooled.transform.SetParent(null, false);
                    pooled.gameObject.SetActive(true);
                    return pooled;
                }
            }

            if (prefab == null)
            {
                return null;
            }

            var go = Object.Instantiate(prefab);
            var item = go.GetComponent<CardItem>();
            if (item == null)
            {
                item = go.AddComponent<CardItem>();
            }

            return item;
        }

        public static void Return(CardItem item)
        {
            if (item == null)
            {
                return;
            }

            item.ResetForPool();
            item.gameObject.SetActive(false);
            item.transform.SetParent(PoolRoot(), false);
            _items.Push(item);
        }

        private static Transform PoolRoot()
        {
            if (_root == null)
            {
                var go = new GameObject("CardItemPool");
                go.SetActive(false);
                _root = go.transform;
            }

            return _root;
        }
    }
}
