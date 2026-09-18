using System.Collections.Generic;
using System.Threading.Tasks;
using App.Item;
using App.Resources;
using Framework.Assets;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// LevelUI 列表项复用池。界面每次打开都销毁重建，列表项（完整 PlayerItem / Item 卡，
    /// 含 Animator、粒子等大量组件）每次 Instantiate 二十多个是打开时 GC / 耗时尖峰的主因；
    /// 关闭时把实例回收到隐藏根节点，下次打开直接取出复用。
    /// 回收时复位选中动画并清空点击订阅，其余显示内容在 Bind / 刷新流程中全量重写。
    /// </summary>
    public static class LevelItemPool
    {
        private static Transform _root;
        private static readonly Stack<HeroItem> _heroes = new Stack<HeroItem>();
        private static readonly Stack<LevelItemCard> _levels = new Stack<LevelItemCard>();

        /// <summary>
        /// 冷启动预热（健康忠告展示期调用）：按最终数量提前实例化进池，
        /// 首次打开选关界面不再逐个 Instantiate。数量与后续实际不一致时由 Rent 兜底补建。
        /// </summary>
        public static async Task PrewarmAsync(IResourceService resources, int heroCount, int levelCount)
        {
            if (resources == null || (heroCount <= 0 && levelCount <= 0))
            {
                return;
            }

            if (_heroes.Count >= heroCount && _levels.Count >= levelCount)
            {
                return;
            }

            GameObject prefab;
            try
            {
                prefab = await resources.LoadAsync<GameObject>(ResResourcePaths.LevelUI);
            }
            catch (System.Exception ex)
            {
                AppLog.Warn(LogChannel.UI, $"LevelItemPool prewarm failed to load LevelUI prefab: {ex.Message}");
                return;
            }

            if (prefab == null)
            {
                return;
            }

            // 英雄卡模板在 LevelUI 预制体内（heroItem 节点）；
            // 难度卡模板是嵌套的 IevelItem 预制体实例，直接按其资源路径加载。
            GameObject levelPrefab = null;
            if (levelCount > 0)
            {
                try
                {
                    levelPrefab = await resources.LoadAsync<GameObject>(ResResourcePaths.LevelItem);
                }
                catch (System.Exception ex)
                {
                    AppLog.Warn(LogChannel.UI, $"LevelItemPool prewarm failed to load IevelItem prefab: {ex.Message}");
                }
            }

            var root = PoolRoot();
            Prewarm(_heroes, FindDeep(prefab.transform, "heroItem"), heroCount, root);
            Prewarm(_levels, levelPrefab != null ? levelPrefab.transform : null, levelCount, root);
        }

        private static void Prewarm<T>(Stack<T> pool, Transform template, int count, Transform root)
            where T : Component
        {
            if (template == null)
            {
                return;
            }

            while (pool.Count < count)
            {
                var item = Object.Instantiate(template.gameObject, root, false).GetComponent<T>();
                if (item == null)
                {
                    return;
                }

                item.gameObject.SetActive(false);
                pool.Push(item);
            }
        }

        private static Transform FindDeep(Transform root, string nodeName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == nodeName)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), nodeName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public static HeroItem RentHero(HeroItem template, Transform parent)
        {
            var item = Rent(_heroes);
            if (item == null)
            {
                // 模板处于隐藏态，实例化后需要显式激活。
                item = Object.Instantiate(template.gameObject, parent, false).GetComponent<HeroItem>();
            }
            else
            {
                item.transform.SetParent(parent, false);
            }

            item.gameObject.SetActive(true);
            return item;
        }

        public static LevelItemCard RentLevel(LevelItemCard template, Transform parent)
        {
            var item = Rent(_levels);
            if (item == null)
            {
                item = Object.Instantiate(template.gameObject, parent, false).GetComponent<LevelItemCard>();
            }
            else
            {
                item.transform.SetParent(parent, false);
            }

            item.gameObject.SetActive(true);
            return item;
        }

        public static void ReleaseHero(HeroItem item)
        {
            if (item == null)
            {
                return;
            }

            item.BindClick(null);
            item.SetSelected(false, force: true);
            Release(_heroes, item);
        }

        public static void ReleaseLevel(LevelItemCard item)
        {
            if (item == null)
            {
                return;
            }

            item.ClearClicked();
            // 先停用再复位选中态：停用后 SetSelected 走瞬时归零分支，不会起抬卡协程
            // （协程随停用中断会把 ItemRoot 残留半程位移）。
            item.gameObject.SetActive(false);
            item.SetSelected(false, replay: true);
            item.transform.SetParent(PoolRoot(), false);
            _levels.Push(item);
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

        private static void Release<T>(Stack<T> pool, T item) where T : Component
        {
            item.gameObject.SetActive(false);
            item.transform.SetParent(PoolRoot(), false);
            pool.Push(item);
        }

        private static Transform PoolRoot()
        {
            if (_root == null)
            {
                var go = new GameObject("LevelItemPool");
                go.SetActive(false);
                _root = go.transform;
            }

            return _root;
        }
    }
}
