using UnityEditor;
using UnityEngine;

namespace Framework.Assets.Editor
{
    public static class ResourceLoadModeEditor
    {
        private const string MenuEditorRes = "Res/Load Mode/Editor Res (Assets/Res)";
        private const string MenuBundles = "Res/Load Mode/Streaming AssetBundles";

        [MenuItem(MenuEditorRes, false, 200)]
        private static void SelectEditorRes()
        {
            ResourceLoadMode.SetUseEditorRes(true);
            Debug.Log("Resource load mode: Editor Res — play mode loads from Assets/Res (no bundle build).");
        }

        [MenuItem(MenuEditorRes, true)]
        private static bool SelectEditorResValidate()
        {
            Menu.SetChecked(MenuEditorRes, ResourceLoadMode.UseEditorRes);
            return true;
        }

        [MenuItem(MenuBundles, false, 201)]
        private static void SelectStreamingBundles()
        {
            ResourceLoadMode.SetUseEditorRes(false);
            Debug.Log(
                "Resource load mode: Streaming AssetBundles — play mode loads from StreamingAssets/Bundles. " +
                "Run Res/Build AssetBundles first.");
        }

        [MenuItem(MenuBundles, true)]
        private static bool SelectStreamingBundlesValidate()
        {
            Menu.SetChecked(MenuBundles, !ResourceLoadMode.UseEditorRes);
            return true;
        }
    }
}
