using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GSplat.Tests
{
    /// <summary>
    /// TZ E11-T2 without an extra package: renders are compared against PNGs committed under
    /// Tests/Runtime/GoldenImages/&lt;api&gt;/&lt;name&gt;.png. A missing reference is written and the test is marked
    /// inconclusive, so the first run on a new GPU/API creates the baseline instead of failing. Editor only: on a
    /// device the package folder is not there, and the reference GPU is the developer machine anyway.
    /// </summary>
    public static class GoldenImage
    {
        private const string Folder = "Packages/com.denisislamov.gsplat/Tests/Runtime/GoldenImages";

        /// <summary>
        /// Compares pixel by pixel. <paramref name="maxAverageError"/> is the mean absolute channel difference (0..255)
        /// allowed over the image; <paramref name="maxBadPixelFraction"/> the share of pixels that may differ by more than 32.
        /// </summary>
        public static void Assert(Texture2D actual, string name, float maxAverageError = 1.0f, float maxBadPixelFraction = 0.002f)
        {
            if (!Application.isEditor) NUnit.Framework.Assert.Ignore("Golden images are compared in the editor only.");

            string apiFolder = Path.Combine(Folder, SystemInfo.graphicsDeviceType.ToString());
            string referencePath = Path.Combine(apiFolder, name + ".png");
            byte[] actualPng = actual.EncodeToPNG();

            if (!File.Exists(referencePath))
            {
                Directory.CreateDirectory(apiFolder);
                File.WriteAllBytes(referencePath, actualPng);
                NUnit.Framework.Assert.Inconclusive($"No golden image for '{name}' on {SystemInfo.graphicsDeviceType}; wrote {referencePath}. Review it and commit it.");
            }

            var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                reference.LoadImage(File.ReadAllBytes(referencePath));
                NUnit.Framework.Assert.AreEqual(reference.width, actual.width, "width");
                NUnit.Framework.Assert.AreEqual(reference.height, actual.height, "height");

                Color32[] expected = reference.GetPixels32();
                Color32[] got = actual.GetPixels32();
                long totalError = 0;
                int badPixels = 0;
                for (int pixel = 0; pixel < expected.Length; pixel++)
                {
                    int error = Mathf.Abs(expected[pixel].r - got[pixel].r) + Mathf.Abs(expected[pixel].g - got[pixel].g) + Mathf.Abs(expected[pixel].b - got[pixel].b);
                    totalError += error;
                    if (error > 32 * 3) badPixels++;
                }

                float averageError = totalError / (3f * expected.Length);
                float badFraction = badPixels / (float)expected.Length;
                if (averageError > maxAverageError || badFraction > maxBadPixelFraction)
                {
                    string actualPath = Path.Combine(apiFolder, name + ".actual.png");
                    File.WriteAllBytes(actualPath, actualPng);
                    NUnit.Framework.Assert.Fail($"'{name}' differs from the golden image: average error {averageError:F2} (max {maxAverageError}), bad pixels {badFraction:P2} (max {maxBadPixelFraction:P2}). Actual saved to {actualPath}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        public static Texture2D Capture(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            return texture;
        }
    }
}
