using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using Framework.Assets;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 选中卡牌阴影的实例池。预制体在启动时经 PreloadAsync 预热进资源缓存；
    /// Rent 把阴影挂到卡位下，Return 回收挂回 idleParent，Dispose 销毁池并释放资源引用。
    /// </summary>
    public sealed class CardShadowPool
    {
        private IResourceService _resources;
        private Transform _idleParent;
        private GameObject _prefab;
        private readonly Stack<Transform> _pool = new Stack<Transform>();

        /// <summary>启动时预热 CardShadow 预制体；常驻资源缓存，之后 Rent 的同步加载直接命中缓存。</summary>
        public static async Task PreloadAsync(IResourceService resources)
        {
            if (resources == null)
            {
                return;
            }

            try
            {
                await resources.LoadAsync<GameObject>(ResResourcePaths.CardShadow);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("CardShadow prefab not found at Res/" + ResResourcePaths.CardShadow + ": " + ex.Message);
            }
        }

        public void Bind(IResourceService resources, Transform idleParent)
        {
            _resources = resources;
            _idleParent = idleParent;
        }

        public Transform Rent(Transform point)
        {
            var prefab = LoadPrefab();
            Transform shadow = null;
            while (_pool.Count > 0 && shadow == null)
            {
                shadow = _pool.Pop();
            }

            if (shadow == null)
            {
                if (prefab == null)
                {
                    return null;
                }

                var go = Object.Instantiate(prefab);
                go.name = "CardShadow";
                shadow = go.transform;
            }

            shadow.SetParent(point, false);
            shadow.localPosition = Vector3.zero;
            shadow.localRotation = Quaternion.identity;
            shadow.localScale = Vector3.one;
            shadow.SetAsFirstSibling();
            shadow.gameObject.SetActive(true);
            return shadow;
        }

        public void Return(ref Transform shadow)
        {
            if (shadow == null)
            {
                return;
            }

            shadow.gameObject.SetActive(false);
            shadow.SetParent(_idleParent, false);
            _pool.Push(shadow);
            shadow = null;
        }

        public void Dispose()
        {
            while (_pool.Count > 0)
            {
                var shadow = _pool.Pop();
                if (shadow != null)
                {
                    Object.Destroy(shadow.gameObject);
                }
            }

            if (_resources != null && _prefab != null)
            {
                _resources.Release(ResResourcePaths.CardShadow);
            }

            _prefab = null;
            _resources = null;
            _idleParent = null;
        }

        private GameObject LoadPrefab()
        {
            if (_prefab != null)
            {
                return _prefab;
            }

            if (_resources == null)
            {
                return null;
            }

            try
            {
                _prefab = _resources.LoadAsync<GameObject>(ResResourcePaths.CardShadow).GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("CardShadow prefab not found at Res/" + ResResourcePaths.CardShadow + ": " + ex.Message);
                return null;
            }

            return _prefab;
        }
    }
}
