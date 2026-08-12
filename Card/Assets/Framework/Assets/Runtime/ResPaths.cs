namespace Framework.Assets
{
    /// <summary>
    /// Project paths for assets under Assets/Res (source of AssetBundle builds).
    /// </summary>
    public static class ResPaths
    {
        public const string AssetRoot = "Assets/Res";

        public static string KeyToAssetBasePath(string key) => $"{AssetRoot}/{key}";
    }
}
