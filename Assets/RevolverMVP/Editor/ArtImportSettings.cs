using UnityEditor;
namespace RevolverMVP
{
    // Keep illustrated pixels crisp and avoid compression rings on the brass outlines.
    public sealed class ArtImportSettings : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith("Assets/RevolverMVP/Resources/Art/")) return;
            var importer=(TextureImporter)assetImporter;
            importer.textureType=TextureImporterType.Default;
            importer.filterMode=UnityEngine.FilterMode.Point;
            importer.mipmapEnabled=false;
            importer.alphaIsTransparency=true;
            importer.wrapMode=UnityEngine.TextureWrapMode.Clamp;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=2048;
        }
    }
}

