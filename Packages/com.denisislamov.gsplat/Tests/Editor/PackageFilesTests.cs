using System.IO;
using NUnit.Framework;

namespace GSplat.Tests
{
    /// <summary>
    /// The package carries its own CHANGELOG.md and LICENSE.md (Package Manager shows them); in the example project
    /// they must match the repository's root files, otherwise one of the two goes stale.
    /// </summary>
    public sealed class PackageFilesTests
    {
        private const string Package = "Packages/com.denisislamov.gsplat";

        [Test]
        public void ChangelogMatchesTheRepositoryRoot()
        {
            if (!File.Exists("CHANGELOG.md")) Assert.Ignore("Not the example project: no root CHANGELOG.md.");
            Assert.AreEqual(File.ReadAllText("CHANGELOG.md"), File.ReadAllText(Package + "/CHANGELOG.md"), "copy the root CHANGELOG.md into the package");
        }

        [Test]
        public void LicenseMatchesTheRepositoryRoot()
        {
            if (!File.Exists("LICENSE")) Assert.Ignore("Not the example project: no root LICENSE.");
            Assert.AreEqual(File.ReadAllText("LICENSE"), File.ReadAllText(Package + "/LICENSE.md"), "copy the root LICENSE into the package as LICENSE.md");
        }

        [Test]
        public void EverySampleInTheManifestExists()
        {
            string manifest = File.ReadAllText(Package + "/package.json");
            foreach (string path in new[] { "Samples~/Viewer Scene", "Samples~/Study Room 100k" })
            {
                Assert.IsTrue(manifest.Contains(path), path + " is listed in package.json");
                Assert.IsTrue(Directory.Exists(Package + "/" + path), path + " exists");
                Assert.IsTrue(Directory.GetFiles(Package + "/" + path, "*.unity").Length == 1, path + " has one scene");
            }
        }
    }
}
