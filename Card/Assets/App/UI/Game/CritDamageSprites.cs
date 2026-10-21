using App.Atlas;
using App.Resources;
using Framework.Log;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 暴击伤害数字：用 Critdamage 图集的减号和 Zero–Nine。
    /// 整体比模板高 RaiseY；字间距比贴图宽少 SpacingShrink；从第二位起每位再抬 StepRaiseY。
    /// </summary>
    public static class CritDamageSprites
    {
        public const float RaiseY = 5f;
        public const float SpacingShrink = 40f;
        public const float StepRaiseY = 5f;

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

                var index = written;
                var token = index == 0 ? "MinusSign" : DigitNames[text[index - 1] - '0'];
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

                // 首位贴模板基线；后续每位再抬 5px，形成轻微上扬。
                rect.anchoredPosition = new Vector2(x, y + index * StepRaiseY);
                child.gameObject.SetActive(sprite != null);
                // 字距相对贴图宽收缩 40，避免暴击数字排得过开。
                x += rect.rect.width - SpacingShrink;
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
