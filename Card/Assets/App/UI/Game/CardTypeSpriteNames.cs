using App.Atlas;
using App.Game;
using App.Resources;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// <c>Altas/CardType</c> 里牌型图标 / 名称图的 sprite 名。
    /// 对局 HUD、规则表（WinTip）与商店「牌型倍率提升」弹窗共用，避免三处各写一份 switch。
    /// </summary>
    public static class CardTypeSpriteNames
    {
        /// <summary>牌型图标（cardinfoItem 的整块图，含牌型样式）。</summary>
        public static string Icon(HandType type)
        {
            switch (type)
            {
                case HandType.Pair: return "PairIcon";
                case HandType.Straight: return "StraightIcon";
                case HandType.Flush: return "SameSuitIcon";
                case HandType.StraightFlush: return "FlushIcon";
                case HandType.ThreeOfAKind: return "LeopardIcon";
                default: return "HighCardIcon";
            }
        }

        /// <summary>牌型名称图（纯文字）。</summary>
        public static string Label(HandType type)
        {
            switch (type)
            {
                case HandType.Pair: return "Pair";
                case HandType.Straight: return "Straight";
                case HandType.Flush: return "SameSuit";
                case HandType.StraightFlush: return "Flush";
                case HandType.ThreeOfAKind: return "Leopard";
                default: return "HighCard";
            }
        }

        /// <summary>从图集取牌型图标；图集未就绪或缺图返回 null。</summary>
        public static Sprite GetIcon(IAtlasService atlas, HandType type)
        {
            if (atlas == null)
            {
                return null;
            }

            atlas.TryGetSprite(ResResourcePaths.CardTypeAtlas, Icon(type), out var sprite);
            return sprite;
        }
    }
}
