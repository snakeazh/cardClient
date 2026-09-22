using App.Resources;
using CardShare.Contracts.Config;
using Framework.Log;
using UnityEngine;

namespace App.Atlas
{
    /// <summary>
    /// playitem 图集（Altas/playitem，源图 Assets/Sprites/playeritem）的品质卡背三层：
    /// PlayerItem 的 card/bg、card/bg/direct、card/bg/direct/di 分别对应 {品质}CardFrameBack{1,2,3}。
    /// 稀有在该图集中为 Blue 系列且不带 Back 后缀（BlueCardFrame{1,2,3}）。
    /// </summary>
    public static class PlayItemSpriteLibrary
    {
        private static IAtlasService _atlas;

        public static void Bind(IAtlasService atlas)
        {
            _atlas = atlas;
        }

        /// <summary>品质卡背分层图：layer 1=bg、2=direct、3=di。缺图返回 null。</summary>
        public static Sprite GetCardFrameBack(QualityType type, int layer)
        {
            return Get(SpriteName(type, layer));
        }

        /// <summary>按图集内 sprite 名取图（怪物 BaseMap / HealthBar 等）。缺名或缺图返回 null。</summary>
        public static Sprite Get(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            if (_atlas == null)
            {
                AppLog.Warn(LogChannel.Atlas, "IAtlasService is not ready. Preload atlases in AppBootstrap.");
                return null;
            }

            return _atlas.GetSprite(ResResourcePaths.PlayItemAtlas, spriteName);
        }

        private static string SpriteName(QualityType type, int layer)
        {
            switch (type)
            {
                case QualityType.Epic:
                    return $"EpicCardFrameBack{layer}";
                case QualityType.Legend:
                    return $"LegendCardFrameBack{layer}";
                case QualityType.Rare:
                    return $"BlueCardFrame{layer}";
                default:
                    return $"OrdinaryCardFrameBack{layer}";
            }
        }
    }
}
