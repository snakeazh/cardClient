using System.Threading.Tasks;

namespace Framework.Assets
{
    /// <summary>
    /// Resource system bootstrap: must call InitializeAsync before LoadAsync.
    /// </summary>
    public interface IResourceInitializer
    {
        bool IsInitialized { get; }

        /// <summary>Bundle version from StreamingAssets/Bundles/version.txt.</summary>
        string BundleVersion { get; }

        /// <summary>Root folder containing bundle files (StreamingAssets/Bundles).</summary>
        string BundleRoot { get; }

        Task InitializeAsync();

        void ReleaseAll();
    }
}
