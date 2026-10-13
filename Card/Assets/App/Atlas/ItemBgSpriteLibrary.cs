using App.Resources;
using CardShare.Contracts.Config;
using Framework.Log;
using UnityEngine;

namespace App.Atlas
{
    /// <summary>
    /// 原 Altas/ItemBg 资源已并入 Altas/playitem（源图 Assets/Sprites/playeritem）。
    /// 提供方形卡面/卡背、长条框、底框遮罩与按名取图；命名与 playitem 内 sprite 一致。
    /// </summary>
    public static class ItemBgSpriteLibrary
    {
        public const string BaseMaskSpriteName = "BlackBaseFrameMask";

        private static IAtlasService _atlas;

        public static void Bind(IAtlasService atlas)
        {
            _atlas = atlas;
        }

        /// <summary>
        /// 方形卡片外框。playitem 无独立 CardFrame，仅保留 API；缺图返回 null（不改预制体当前图）。
        /// </summary>
        public static Sprite GetCardFrame(QualityType type)
        {
            // 历史名 OrdinaryCardFrame 等已删除；避免向图集查不存在的名字刷 Warn。
            return null;
        }

        /// <summary>CardBG 单层卡背：取 playitem 三层卡背的第 1 层（bg）。</summary>
        public static Sprite GetCardFrameBack(QualityType type)
        {
            return PlayItemSpriteLibrary.GetCardFrameBack(type, 1);
        }

        /// <summary>
        /// 长条矩形品质边框（PlayerItem attack/heart 等）。
        /// Ordinary→Yellow / Rare→Blue / Epic→Purple / Legend→Red。
        /// </summary>
        public static Sprite GetRectangleFrame(QualityType type)
        {
            return GetSprite(RectangleSpriteName(type));
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

        private static string RectangleSpriteName(QualityType type)
        {
            switch (type)
            {
                case QualityType.Rare:
                    return "BlueRectangleFrame";
                case QualityType.Epic:
                    return "PurpleRectangleFrame";
                case QualityType.Legend:
                    return "RedRectangleFrame";
                default:
                    return "YellowRectangleFrame";
            }
        }

        private static Sprite GetSprite(string spriteName)
        {
            if (_atlas == null)
            {
                AppLog.Warn(LogChannel.Atlas, "IAtlasService is not ready. Preload atlases in AppBootstrap.");
                return null;
            }

            if (_atlas.TryGetSprite(ResResourcePaths.PlayItemAtlas, spriteName, out var sprite) && sprite != null)
            {
                return sprite;
            }

            return null;
        }
    }
}
