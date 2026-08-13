using System.Collections.Generic;
using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 牌面资源命名：101 红心A、201 方片A、301 草花A、401 黑桃A；
    /// 同花色 01→13 对应 A→K。背面为 CardBack。
    /// </summary>
    public static class CardSpriteLibrary
    {
        private static Sprite _back;
        private static readonly Dictionary<int, Sprite> _faces = new Dictionary<int, Sprite>();

        public static Sprite Back
        {
            get
            {
                if (_back == null)
                {
                    _back = UnityEngine.Resources.Load<Sprite>("CardBack");
                }

                return _back;
            }
        }

        public static Sprite GetFace(Card card)
        {
            var id = card.ResourceId;
            if (_faces.TryGetValue(id, out var cached) && cached != null)
            {
                return cached;
            }

            var sprite = UnityEngine.Resources.Load<Sprite>(id.ToString());
            if (sprite != null)
            {
                _faces[id] = sprite;
            }

            return sprite != null ? sprite : Back;
        }
    }
}
