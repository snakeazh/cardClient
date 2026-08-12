namespace Framework.Assets
{
    /// <summary>
    /// Editor play mode: load directly from Assets/Res instead of StreamingAssets bundles.
    /// </summary>
    public static class ResourceLoadMode
    {
        public const string EditorPrefsKey = "Framework.Assets.UseEditorRes";

        public static bool UseEditorRes
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(EditorPrefsKey, true);
#else
                return false;
#endif
            }
        }

#if UNITY_EDITOR
        public static void SetUseEditorRes(bool value)
        {
            UnityEditor.EditorPrefs.SetBool(EditorPrefsKey, value);
        }
#endif
    }
}
