using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 牌面资源命名：101 红心A、201 方片A、301 草花A、401 黑桃A；
    /// 同花色 01→13 对应 A→K（如 111=红心J，113=红心K）。
    /// </summary>
    public static class CardSpriteLibrary
    {
        private static Sprite _back;
        private static Sprite _fallback;

        public static Sprite Back
        {
            get
            {
                if (_back == null)
                {
                    _back = UnityEngine.Resources.Load<Sprite>("back")
                            ?? UnityEngine.Resources.Load<Sprite>("000")
                            ?? FallbackFace;
                }

                return _back;
            }
        }

        public static Sprite GetFace(Card card)
        {
            var sprite = UnityEngine.Resources.Load<Sprite>(card.ResourceId.ToString());
            if (sprite != null)
            {
                return sprite;
            }

            return FallbackFace;
        }

        private static Sprite FallbackFace
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = UnityEngine.Resources.Load<Sprite>("101");
                }

                return _fallback;
            }
        }
    }
}
