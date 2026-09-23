using System;
using System.Collections.Generic;
using UnityEditor;

namespace App.Editor
{
    /// <summary>
    /// 图集源图导入规则：SpriteAtlas 打包读取的是源图导入后的像素，源图若为压缩格式会二次有损
    /// 并触发 "Source Texture ... is using compressed format" 警告，平台覆盖的 maxTextureSize
    /// 还会截断打包分辨率。打进图集的源图一律导入为未压缩、不带平台覆盖；
    /// 运行时像素只来自图集纹理，源图导入设置不影响真机包体与内存。
    /// 打包对象动态取自 Res/Altas 下各图集的依赖纹理（与 AtlasService 预加载同一目录），新增图集自动生效。
    /// </summary>
    public class AtlasSourceTexturePostprocessor : AssetPostprocessor
    {
        private static readonly string[] KnownPlatforms = { "Standalone", "Android", "WebGL", "iOS" };
        private static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga" };

        private static HashSet<string> _packedTexturePaths;

        private void OnPreprocessTexture()
        {
            if (!GetPackedTexturePaths().Contains(assetPath))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            }

            for (var i = 0; i < KnownPlatforms.Length; i++)
            {
                var settings = importer.GetPlatformTextureSettings(KnownPlatforms[i]);
                if (settings.overridden)
                {
                    settings.overridden = false;
                    importer.SetPlatformTextureSettings(settings);
                }
            }
        }

        /// <summary>Res/Altas 下所有图集实际打包的纹理路径；图集或图片增删移动后由 OnPostprocessAllAssets 失效重建。</summary>
        private static HashSet<string> GetPackedTexturePaths()
        {
            if (_packedTexturePaths != null)
            {
                return _packedTexturePaths;
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var atlasFolder = "Assets/Res/" + App.Atlas.AtlasService.AtlasBundleName;
            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { atlasFolder }))
            {
                var atlasPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!atlasPath.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var dependency in AssetDatabase.GetDependencies(atlasPath, true))
                {
                    if (HasTextureExtension(dependency))
                    {
                        paths.Add(dependency);
                    }
                }
            }

            // 工程冷导入时图集可能晚于纹理导入，依赖还不完整；空结果不缓存，下次导入重查。
            if (paths.Count > 0)
            {
                _packedTexturePaths = paths;
            }

            return paths;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (HasRelevantChange(importedAssets) || HasRelevantChange(deletedAssets) ||
                HasRelevantChange(movedAssets) || HasRelevantChange(movedFromAssetPaths))
            {
                _packedTexturePaths = null;
            }
        }

        private static bool HasRelevantChange(string[] paths)
        {
            for (var i = 0; i < paths.Length; i++)
            {
                if (paths[i].EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase) ||
                    HasTextureExtension(paths[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTextureExtension(string path)
        {
            for (var i = 0; i < TextureExtensions.Length; i++)
            {
                if (path.EndsWith(TextureExtensions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
