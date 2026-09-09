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
    /// 把 <c>0/4</c> 拆成 <c>Altas/ShopNum</c> 图集里的数字与 Slash。
    /// </summary>
    public static class ShopNumSprites
    {
        private const string SlashSprite = "Slash";
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

            EnsureLayout(root);
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
                    atlas.TryGetSprite(ResResourcePaths.ShopNumAtlas, Tokens[childIndex], out sprite);
                }

                if (sprite == null)
                {
                    AppLog.Warn(LogChannel.Atlas, $"ShopNum missing '{Tokens[childIndex]}'.");
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

        private static void EnsureLayout(Transform root)
        {
            var layout = root.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
            {
                layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            }

            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = -8f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childScaleWidth = false;
            layout.childScaleHeight = false;
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

                if (c == '/' || c == '\\')
                {
                    tokens.Add(SlashSprite);
                    continue;
                }

                if (c >= '0' && c <= '9')
                {
                    tokens.Add(c.ToString());
                }
            }
        }
    }
}
