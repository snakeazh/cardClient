using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Game;
using App.Item;
using App.Resources;
using Framework.Assets;
using Framework.Log;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 通用卡槽复用池：图鉴（收藏/遗物页 ItemCard、怪物页 PlayerItem）与天赋页（ItemCard）
    /// 的虚拟化列表槽位在视图关闭时回收复用，避免每次打开重新 Instantiate 一批卡片。
    /// 租用后视图负责一次性装饰（缩放、关阴影动画、订阅点击），回收时已做状态复位，
    /// 其余显示内容在 Bind 流程中全量重写。
    /// PlayerItem 池仅供图鉴怪物页使用（攻/血块已隐藏、补了点击 Button），勿与其他用途混用。
    /// </summary>
    public static class UiCardPool
    {
        private static Transform _root;
        private static readonly Stack<ItemCard> _itemCards = new Stack<ItemCard>();
        private static readonly Stack<PlayerItem> _playerItems = new Stack<PlayerItem>();

        /// <summary>
        /// 冷启动预热（健康忠告展示期调用）：按视口可见量提前实例化进池，
        /// 首次打开图鉴/天赋不再逐个 Instantiate。数量与实际不一致时由 Rent 兜底补建。
        /// </summary>
        public static async Task PrewarmAsync(IResourceService resources, int itemCardCount, int playerItemCount)
        {
            if (resources == null || (itemCardCount <= 0 && playerItemCount <= 0))
            {
                return;
            }

            if (_itemCards.Count >= itemCardCount && _playerItems.Count >= playerItemCount)
            {
                return;
            }

            var root = PoolRoot();
            if (_itemCards.Count < itemCardCount)
            {
                var prefab = await LoadPrefab(resources, ResResourcePaths.Item);
                Prewarm(_itemCards, prefab, itemCardCount, root);
            }

            if (_playerItems.Count < playerItemCount)
            {
                var prefab = await LoadPrefab(resources, ResResourcePaths.PlayerItem);
                Prewarm(_playerItems, prefab, playerItemCount, root);
            }
        }

        public static ItemCard RentItemCard(GameObject prefab, Transform parent)
        {
            var card = Rent(_itemCards);
            if (card == null)
            {
                if (prefab == null)
                {
                    return null;
                }

                card = Object.Instantiate(prefab, parent, false).GetComponent<ItemCard>();
            }
            else
            {
                card.transform.SetParent(parent, false);
            }

            if (card != null)
            {
                card.gameObject.SetActive(true);
            }

            return card;
        }

        public static PlayerItem RentPlayerItem(GameObject prefab, Transform parent)
        {
            var card = Rent(_playerItems);
            if (card == null)
            {
                if (prefab == null)
                {
                    return null;
                }

                card = Object.Instantiate(prefab, parent, false).GetComponent<PlayerItem>();
            }
            else
            {
                card.transform.SetParent(parent, false);
            }

            if (card != null)
            {
                card.gameObject.SetActive(true);
            }

            return card;
        }

        /// <summary>回收 ItemCard：先停用（停粒子/动画）再复位选中态、缩放、品质特效、等级角标与图标染色，
        /// 清点击订阅（旧视图已销毁，残留订阅会调到已销毁对象）。缩放回池即复位 1，
        /// 非默认缩放的视图（图鉴 0.9）须在租用后自行重设。</summary>
        public static void ReleaseItemCard(ItemCard card)
        {
            if (card == null)
            {
                return;
            }

            card.gameObject.SetActive(false);
            card.ClearClicked();
            card.SetSelected(false);
            card.transform.localScale = Vector3.one;
            card.ShowQualityFx(QualityType.Ordinary);
            card.SetLevel(null);
            card.SetIconColor(Color.white);
            card.transform.SetParent(PoolRoot(), false);
            _itemCards.Push(card);
        }

        /// <summary>回收 PlayerItem：关抬卡 Animator、Y 归零，并清掉图鉴补的点击 Button 订阅
        /// （闭包捕获旧视图，不清会把已销毁视图留在内存里）。</summary>
        public static void ReleasePlayerItem(PlayerItem card)
        {
            if (card == null)
            {
                return;
            }

            card.gameObject.SetActive(false);
            card.SetSelectLift(false, HeroItem.SelectAnim, HeroItem.DefaultAnim);
            var button = card.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
            }

            card.transform.SetParent(PoolRoot(), false);
            _playerItems.Push(card);
        }

        private static async Task<GameObject> LoadPrefab(IResourceService resources, string path)
        {
            try
            {
                return await resources.LoadAsync<GameObject>(path);
            }
            catch (System.Exception ex)
            {
                AppLog.Warn(LogChannel.UI, $"UiCardPool prewarm failed to load {path}: {ex.Message}");
                return null;
            }
        }

        private static void Prewarm<T>(Stack<T> pool, GameObject prefab, int count, Transform root)
            where T : Component
        {
            if (prefab == null)
            {
                return;
            }

            while (pool.Count < count)
            {
                var item = Object.Instantiate(prefab, root, false).GetComponent<T>();
                if (item == null)
                {
                    return;
                }

                item.gameObject.SetActive(false);
                pool.Push(item);
            }
        }

        private static T Rent<T>(Stack<T> pool) where T : Component
        {
            // Unity 假空：池根节点随场景销毁等情况，弹出已销毁引用直接跳过。
            while (pool.Count > 0)
            {
                var item = pool.Pop();
                if (item != null)
                {
                    return item;
                }
            }

            return null;
        }

        private static Transform PoolRoot()
        {
            if (_root == null)
            {
                var go = new GameObject("UiCardPool");
                go.SetActive(false);
                _root = go.transform;
            }

            return _root;
        }
    }
}
