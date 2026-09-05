using UnityEditor.AssetImporters;

namespace GSplat.Editor
{
    [ScriptedImporter(1, "ply")]
    public sealed class PlyImporter : SplatImporterBase
    {
        protected override SplatCoordinateSystem DefaultCoordinateSystem => SplatCoordinateSystem.Rdf;

        protected override SplatCloud Decode(byte[] bytes)
        {
            return PlyReader.Read(bytes);
        }
    }
}
