using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.U2D;

namespace App.Atlas
{
    /// <summary>
    /// Preloaded SpriteAtlas cache. Load atlases at startup, then GetSprite by atlas key + sprite name.
    /// </summary>
    public interface IAtlasService
    {
        bool IsLoaded { get; }

        Task PreloadAsync();

        SpriteAtlas GetAtlas(string atlasKey);

        Sprite GetSprite(string atlasKey, string spriteName);

        bool TryGetSprite(string atlasKey, string spriteName, out Sprite sprite);
    }
}
