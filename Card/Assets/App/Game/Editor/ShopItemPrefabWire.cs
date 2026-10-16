using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace App.Game.Editor
{
    /// <summary>
    /// 把 ShopItem 预制体上的 card_Name / card_icon / IconBG / IconTitleBG / card_Circle / priceValue(goldNum) / gold / Button 直接挂到序列化字段。
    /// </summary>
    public static class ShopItemPrefabWire
    {
        private const string PrefabPath = "Assets/Res/UI/Icon/ShopItem.prefab";

        [InitializeOnLoadMethod]
        private static void AutoWire()
        {
            EditorApplication.delayCall += WireIfNeeded;
        }

        [MenuItem("Tools/Wire ShopItem Prefab")]
        private static void WireFromMenu()
        {
            if (WirePrefab(force: true))
            {
                Debug.Log("[ShopItem] 预制体引用已挂载: " + PrefabPath);
            }
        }

        private static void WireIfNeeded()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += WireIfNeeded;
                return;
            }

            WirePrefab(force: false);
        }

        private static bool WirePrefab(bool force)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var item = root.GetComponent<ShopItem>();
                if (item == null)
                {
                    return false;
                }

                var so = new SerializedObject(item);
                var normalRoot = FindNamed<Transform>(root.transform, "Item");
                var changed = Assign(so, "cardName", FindNamed<TMP_Text>(root.transform, "card_Name"), force);
                changed |= Assign(so, "cardIcon", FindNamed<Image>(root.transform, "card_icon"), force);
                changed |= Assign(so, "iconBg", FindNamed<Image>(root.transform, "IconBG"), force);
                changed |= Assign(so, "iconTitleBg", FindNamed<Image>(root.transform, "IconTitleBG"), force);
                changed |= Assign(so, "cardCircle", FindNamed<Image>(root.transform, "card_Circle"), force);
                // BuySlot 子树里也有同名 priceValue，正常卡价格文本须限定在 Item 子树内查找。
                changed |= Assign(
                    so,
                    "goldNum",
                    FindNamed<TMP_Text>(normalRoot, "priceValue") ?? FindNamed<TMP_Text>(normalRoot, "goldNum"),
                    force);
                changed |= Assign(so, "gold", FindNamed<Image>(root.transform, "gold"), force);
                changed |= Assign(so, "lv", FindNamed<TMP_Text>(root.transform, "lv"), force);
                changed |= Assign(so, "hasNum", FindNamed<TMP_Text>(root.transform, "hasNum"), force);
                changed |= Assign(so, "button", item.GetComponent<Button>(), force);
                changed |= Assign(so, "normalRoot", normalRoot != null ? normalRoot.gameObject : null, force);
                changed |= Assign(
                    so,
                    "emptyRoot",
                    FindNamed<Transform>(root.transform, "IsEmpty") is { } empty ? empty.gameObject : null,
                    force);
                changed |= Assign(
                    so,
                    "buySlotRoot",
                    FindNamed<Transform>(root.transform, "BuySlot") is { } buySlot ? buySlot.gameObject : null,
                    force);
                var battleHor = FindNamed<RectTransform>(root.transform, "battleHor");
                changed |= Assign(so, "battleHor", battleHor, force);
                changed |= Assign(so, "bondIcon", FindNamed<Image>(battleHor, "icon"), force);
                if (!changed)
                {
                    return false;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool Assign(SerializedObject so, string field, Object value, bool force)
        {
            var prop = so.FindProperty(field);
            if (prop == null || value == null)
            {
                return false;
            }

            if (!force && prop.objectReferenceValue != null)
            {
                return false;
            }

            if (prop.objectReferenceValue == value)
            {
                return false;
            }

            prop.objectReferenceValue = value;
            return true;
        }

        private static T FindNamed<T>(Transform root, string nodeName) where T : Component
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == nodeName)
            {
                return root.GetComponent<T>();
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindNamed<T>(root.GetChild(i), nodeName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
