using System.Collections.Generic;
using App.Atlas;
using App.Resources;
using Framework.Log;
using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 牌面资源命名：101 红心A、201 方片A、301 草花A、401 黑桃A；
    /// 同花色 01→13 对应 A→K。背面为 CardBack。贴图来自预加载图集 Altas/Card。
    /// </summary>
    public static class CardSpriteLibrary
    {
        public const string BackSpriteName = "CardBack";

        private static IAtlasService _atlas;
        private static Sprite _back;
        private static readonly Dictionary<int, Sprite> _faces = new Dictionary<int, Sprite>();

        public static void Bind(IAtlasService atlas)
        {
            _atlas = atlas;
            _back = null;
            _faces.Clear();
        }

        public static Sprite Back
        {
            get
            {
                if (_back == null)
                {
                    _back = GetSprite(BackSpriteName);
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

            var sprite = GetSprite(id.ToString());
            if (sprite != null)
            {
                _faces[id] = sprite;
            }

            return sprite != null ? sprite : Back;
        }

        private static Sprite GetSprite(string spriteName)
        {
            if (_atlas == null)
            {
                AppLog.Warn(LogChannel.Game, "IAtlasService is not ready. Preload atlases in AppBootstrap.");
                return null;
            }

            return _atlas.GetSprite(ResResourcePaths.CardAtlas, spriteName);
        }
    }
}
