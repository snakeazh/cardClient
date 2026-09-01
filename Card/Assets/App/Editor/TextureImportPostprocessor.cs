using System;
using UnityEditor;

namespace App.Editor
{
    /// <summary>
    /// 图片导入规则：放入 Assets/Res/ 的新图片自动把 Texture Type 从 Default 改为 Sprite (2D and UI)。
    /// 只处理 Default 类型，手动设为 NormalMap、Cursor 等其它类型的不会被覆盖；
    /// Res 目录以外的图片（含 Plugins）不受影响。
    /// </summary>
    public class TextureImportPostprocessor : AssetPostprocessor
    {
        private const string ResFolder = "Assets/Res/";

        private void OnPreprocessTexture()
        {
            var importer = (TextureImporter)assetImporter;
            if (!assetPath.StartsWith(ResFolder, StringComparison.OrdinalIgnoreCase))
                return;
            if (importer.textureType != TextureImporterType.Default)
                return;

            importer.textureType = TextureImporterType.Sprite;
        }
    }
}
