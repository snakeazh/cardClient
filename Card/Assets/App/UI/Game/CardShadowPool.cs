using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using Framework.Assets;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 选中卡牌阴影的实例池。预制体在启动时经 PreloadAsync 预热；
    /// Rent 把阴影挂到卡位下，Return 回收挂回 idleParent，Dispose 销毁池内实例。
    /// </summary>
    public sealed class CardShadowPool
    {
        private static GameObject _prefab;
        private Transform _idleParent;
        private readonly Stack<Transform> _pool = new Stack<Transform>();

        /// <summary>启动时预热 CardShadow 预制体，常驻资源缓存。</summary>
        public static async Task PreloadAsync(IResourceService resources)
        {
            if (resources == null || _prefab != null)
            {
                return;
            }

            try
            {
                _prefab = await resources.LoadAsync<GameObject>(ResResourcePaths.CardShadow);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("CardShadow prefab not found at Res/" + ResResourcePaths.CardShadow + ": " + ex.Message);
            }
        }

        public void Bind(Transform idleParent)
        {
            _idleParent = idleParent;
        }

        public Transform Rent(Transform point)
        {
            Transform shadow = null;
            while (_pool.Count > 0 && shadow == null)
            {
                shadow = _pool.Pop();
            }

            if (shadow == null)
            {
                if (_prefab == null)
                {
                    return null;
                }

                var go = Object.Instantiate(_prefab);
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

            var instance = shadow;
            shadow = null;

            // 父节点正在销毁时禁止 SetParent（GameHud OnDestroy 会先失活）。
            if (_idleParent == null || !_idleParent.gameObject.activeInHierarchy)
            {
                if (instance != null)
                {
                    Object.Destroy(instance.gameObject);
                }

                return;
            }

            instance.gameObject.SetActive(false);
            instance.SetParent(_idleParent, false);
            _pool.Push(instance);
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

            _idleParent = null;
        }
    }
}
