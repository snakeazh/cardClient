using App.Atlas;
using App.Resources;
using Framework.Log;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>暴击伤害数字：用 Critdamage 图集的减号和 Zero–Nine，每张图比模板高 5 像素。</summary>
    public static class CritDamageSprites
    {
        public const float RaiseY = 5f;

        private static readonly string[] DigitNames =
        {
            "Zero", "One", "Two", "Three", "Four", "Five", "six", "Seven", "Eight", "Nine"
        };

        public static void Apply(IAtlasService atlas, Transform numShow, int damage)
        {
            if (numShow == null)
            {
                return;
            }

            var text = MathMax(damage).ToString();
            var count = text.Length + 1;
            var template = EnsureDigits(numShow, count);
            if (template == null)
            {
                return;
            }

            var home = template.anchoredPosition;
            var x = home.x;
            var y = home.y + RaiseY;
            var written = 0;
            for (var i = 0; i < numShow.childCount; i++)
            {
                var child = numShow.GetChild(i);
                var image = child != null ? child.GetComponent<Image>() : null;
                var rect = child as RectTransform;
                if (image == null || rect == null)
                {
                    continue;
                }

                if (written >= count)
                {
                    child.gameObject.SetActive(false);
                    continue;
                }

                var token = written == 0 ? "MinusSign" : DigitNames[text[written - 1] - '0'];
                written++;
                Sprite sprite = null;
                atlas?.TryGetSprite(ResResourcePaths.CritDamageAtlas, token, out sprite);
                if (sprite == null)
                {
                    AppLog.Warn(LogChannel.Atlas, "Critdamage missing '" + token + "'.");
                }

                image.sprite = sprite;
                image.enabled = sprite != null;
                image.preserveAspect = true;
                image.raycastTarget = false;
                if (sprite != null)
                {
                    image.SetNativeSize();
                }

                rect.anchoredPosition = new Vector2(x, y);
                child.gameObject.SetActive(sprite != null);
                x += rect.rect.width;
            }
        }

        private static int MathMax(int damage)
        {
            return damage < 1 ? 1 : damage;
        }

        private static RectTransform EnsureDigits(Transform numShow, int count)
        {
            RectTransform template = null;
            var have = 0;
            for (var i = 0; i < numShow.childCount; i++)
            {
                var rect = numShow.GetChild(i) as RectTransform;
                if (rect == null || rect.GetComponent<Image>() == null)
                {
                    continue;
                }

                if (template == null)
                {
                    template = rect;
                }

                have++;
            }

            if (template == null)
            {
                return null;
            }

            while (have < count)
            {
                var go = Object.Instantiate(template.gameObject, numShow, false);
                go.name = "digit" + have;
                have++;
            }

            return template;
        }
    }
}
