using System.Collections.Generic;
using App.Atlas;
using App.Resources;
using Framework.Log;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 把 <c>x2.5</c> / <c>x10</c> 拆成 <c>Altas/cardTypeValue</c> 图集里的 ×、数字、Point。
    /// </summary>
    public static class CardTypeValueSprites
    {
        private static readonly List<string> Tokens = new List<string>(8);

        public static void Prepare(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var tmp = root.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.enabled = false;
                tmp.raycastTarget = false;
            }
        }

        public static void Apply(IAtlasService atlas, Transform root, string formatted)
        {
            if (root == null)
            {
                return;
            }

            Prepare(root);
            Tokenize(formatted, Tokens);
            EnsureDigitCount(root, Tokens.Count);
            var childIndex = 0;
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var image = child != null ? child.GetComponent<Image>() : null;
                if (image == null)
                {
                    continue;
                }

                if (childIndex >= Tokens.Count)
                {
                    child.gameObject.SetActive(false);
                    continue;
                }

                Sprite sprite = null;
                if (atlas != null)
                {
                    atlas.TryGetSprite(ResResourcePaths.CardTypeValueAtlas, Tokens[childIndex], out sprite);
                }

                if (sprite == null)
                {
                    AppLog.Warn(LogChannel.Atlas, $"cardTypeValue missing '{Tokens[childIndex]}'.");
                }

                image.sprite = sprite;
                image.enabled = sprite != null;
                image.preserveAspect = true;
                image.raycastTarget = false;
                if (sprite != null)
                {
                    image.SetNativeSize();
                }
                child.gameObject.SetActive(sprite != null);
                childIndex++;
            }
        }

        private static void EnsureDigitCount(Transform root, int count)
        {
            var have = 0;
            for (var i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i).GetComponent<Image>() != null)
                {
                    have++;
                }
            }

            while (have < count)
            {
                var go = new GameObject($"digit{have}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(root, false);
                var image = go.GetComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                have++;
            }
        }

        private static void Tokenize(string formatted, List<string> tokens)
        {
            tokens.Clear();
            if (string.IsNullOrEmpty(formatted))
            {
                return;
            }

            for (var i = 0; i < formatted.Length; i++)
            {
                var c = formatted[i];
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (c == 'x' || c == 'X' || c == '×')
                {
                    tokens.Add("×");
                    continue;
                }

                if (c == '.')
                {
                    tokens.Add("Point");
                    continue;
                }

                if (c == '1' && i + 1 < formatted.Length && formatted[i + 1] == '0')
                {
                    var after = i + 2;
                    if (after >= formatted.Length || !char.IsDigit(formatted[after]))
                    {
                        tokens.Add("10");
                        i++;
                        continue;
                    }
                }

                if (c >= '0' && c <= '9')
                {
                    tokens.Add(c.ToString());
                }
            }
        }
    }
}
