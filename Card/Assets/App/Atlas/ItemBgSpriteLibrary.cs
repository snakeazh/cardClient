using App.Config;
using App.Resources;
using Framework.Log;
using UnityEngine;

namespace App.Atlas
{
    /// <summary>
    /// 品质边框资源命名：Ordinary/Rare/Epic/Legend + CardFrame（方形卡）/ RectangleFrame（长条框），
    /// 黑色底框遮罩 BlackBaseFrameMask。贴图来自预加载图集 Altas/ItemBg。
    /// </summary>
    public static class ItemBgSpriteLibrary
    {
        public const string BaseMaskSpriteName = "BlackBaseFrameMask";

        private static IAtlasService _atlas;

        public static void Bind(IAtlasService atlas)
        {
            _atlas = atlas;
        }

        /// <summary>方形卡片品质边框。</summary>
        public static Sprite GetCardFrame(QualityType type)
        {
            return GetSprite(QualityPrefix(type) + "CardFrame");
        }

        /// <summary>长条矩形品质边框。</summary>
        public static Sprite GetRectangleFrame(QualityType type)
        {
            return GetSprite(QualityPrefix(type) + "RectangleFrame");
        }

        /// <summary>黑色底框遮罩。</summary>
        public static Sprite GetBaseMask()
        {
            return GetSprite(BaseMaskSpriteName);
        }

        private static string QualityPrefix(QualityType type)
        {
            switch (type)
            {
                case QualityType.Rare:
                    return "Rare";
                case QualityType.Epic:
                    return "Epic";
                case QualityType.Legend:
                    return "Legend";
                default:
                    return "Ordinary";
            }
        }

        private static Sprite GetSprite(string spriteName)
        {
            if (_atlas == null)
            {
                AppLog.Warn(LogChannel.Atlas, "IAtlasService is not ready. Preload atlases in AppBootstrap.");
                return null;
            }

            return _atlas.GetSprite(ResResourcePaths.ItemBgAtlas, spriteName);
        }
    }
}
