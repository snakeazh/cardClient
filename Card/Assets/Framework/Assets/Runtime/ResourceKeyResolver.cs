using System;

namespace Framework.Assets
{
    internal static class ResourceKeyResolver
    {
        public static string ResolveBundleName(string key)
        {
            var slash = key.IndexOf('/');
            if (slash <= 0)
            {
                return key.ToLowerInvariant();
            }

            return key.Substring(0, slash).ToLowerInvariant();
        }

        public static string ResolveAssetName(string key)
        {
            var slash = key.LastIndexOf('/');
            return slash >= 0 ? key.Substring(slash + 1) : key;
        }
    }
}
