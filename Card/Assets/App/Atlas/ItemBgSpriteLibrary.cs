using App.Config;
using App.Resources;
using Framework.Log;
using UnityEngine;

namespace App.Atlas
{
    /// <summary>
    /// 品质边框资源命名：Ordinary/Rare/Epic/Legend + CardFrame（方形卡）/ RectangleFrame（长条框）/
    /// CardFrameBack（卡背背景），黑色底框遮罩 BlackBaseFrameMask。贴图来自预加载图集 Altas/ItemBg。
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

        /// <summary>方形卡片品质卡背背景（Item 预制体 CardBG 节点）。</summary>
        public static Sprite GetCardFrameBack(QualityType type)
        {
            return GetSprite(QualityPrefix(type) + "CardFrameBack");
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

        /// <summary>按图集内 sprite 名取图（怪物 BaseMap / HealthBar 等）。缺名或缺图返回 null。</summary>
        public static Sprite Get(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            return GetSprite(spriteName);
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
