using UnityEditor.AssetImporters;

namespace GSplat.Editor
{
    [ScriptedImporter(2, "spz")]
    public sealed class SpzImporter : SplatImporterBase
    {
        protected override SplatCoordinateSystem DefaultCoordinateSystem => SplatCoordinateSystem.Rub;

        protected override SplatCloud Decode(byte[] bytes)
        {
            return SpzReader.Read(bytes);
        }
    }
}
