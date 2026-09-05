using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GSplat.Tests
{
    /// <summary>Writes real files into the project, imports them through the ScriptedImporters and checks the asset.</summary>
    public sealed class ImporterTests
    {
        private const string Folder = "Assets/GSplatImporterTests~Temp";

        [SetUp]
        public void CreateFolder()
        {
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void DeleteFolder()
        {
            AssetDatabase.DeleteAsset(Folder);
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            if (File.Exists(Folder + ".meta")) File.Delete(Folder + ".meta");
            AssetDatabase.Refresh();
        }

        [Test]
        public void SpzFileBecomesAGaussianSplatAsset()
        {
            string path = Folder + "/sample.spz";
            using (SplatCloud cloud = TestClouds.Random(3000, 1))
            {
                File.WriteAllBytes(path, SpzWriter.Write(cloud));
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(path);
            Assert.IsNotNull(asset, "importer produced no asset");
            Assert.AreEqual(3000, asset.SplatCount);
            Assert.AreEqual(3000, asset.SourceSplatCount);
            Assert.AreEqual(0, asset.ShDegree, "default import keeps degree 0");
            Assert.AreEqual(1, asset.ChunkCount);

            var importer = (GSplat.Editor.SpzImporter)AssetImporter.GetAtPath(path);
            Assert.AreEqual(SplatCoordinateSystem.Rub, importer.Options.SourceCoordinateSystem);

            using (GsplatData data = asset.LoadData())
            {
                Assert.AreEqual(3000, data.SplatCount);
            }
        }

        [Test]
        public void PlyFileDefaultsToRdfAxes()
        {
            string path = Folder + "/sample.ply";
            using (SplatCloud cloud = TestClouds.Random(500, 0))
            {
                File.WriteAllBytes(path, PlyReaderTests.WritePly(cloud, false));
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(path);
            Assert.IsNotNull(asset);
            Assert.AreEqual(500, asset.SplatCount);

            var importer = (GSplat.Editor.PlyImporter)AssetImporter.GetAtPath(path);
            Assert.AreEqual(SplatCoordinateSystem.Rdf, importer.Options.SourceCoordinateSystem);
        }

        [Test]
        public void BrokenFileLogsAnImportErrorInsteadOfThrowing()
        {
            string path = Folder + "/broken.spz";
            byte[] bytes;
            using (SplatCloud cloud = TestClouds.Random(50, 0))
            {
                bytes = SpzWriter.Write(cloud);
            }

            for (int byteIndex = 20; byteIndex < bytes.Length; byteIndex++) bytes[byteIndex] = 0x5A; // gzip signature intact, stream garbage
            File.WriteAllBytes(path, bytes);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Could not import broken.spz"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(path));
        }
    }
}
