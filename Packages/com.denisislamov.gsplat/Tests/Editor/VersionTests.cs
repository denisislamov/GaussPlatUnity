using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GSplat.Tests
{
    public sealed class VersionTests
    {
        [Test]
        public void ConstantMatchesPackageJson()
        {
            string json = File.ReadAllText("Packages/com.denisislamov.gsplat/package.json");
            var package = JsonUtility.FromJson<PackageInfo>(json);
            Assert.AreEqual(package.version, GSplatVersion.Current, "bump GSplatVersion.Current and package.json together");
        }

        [System.Serializable]
        private sealed class PackageInfo
        {
            public string version;
        }
    }
}
