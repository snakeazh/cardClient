using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace App.Game.Editor
{
    /// <summary>
    /// 把 ShopItem 预制体上的 card_Name / card_icon / IconBG / IconTitleBG / card_Circle / goldNum / gold / Button 直接挂到序列化字段。
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
                var changed = Assign(so, "cardName", FindNamed<TMP_Text>(root.transform, "card_Name"), force);
                changed |= Assign(so, "cardIcon", FindNamed<Image>(root.transform, "card_icon"), force);
                changed |= Assign(so, "iconBg", FindNamed<Image>(root.transform, "IconBG"), force);
                changed |= Assign(so, "iconTitleBg", FindNamed<Image>(root.transform, "IconTitleBG"), force);
                changed |= Assign(so, "cardCircle", FindNamed<Image>(root.transform, "card_Circle"), force);
                changed |= Assign(so, "goldNum", FindNamed<TMP_Text>(root.transform, "goldNum"), force);
                changed |= Assign(so, "gold", FindNamed<Image>(root.transform, "gold"), force);
                changed |= Assign(so, "button", item.GetComponent<Button>(), force);
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
