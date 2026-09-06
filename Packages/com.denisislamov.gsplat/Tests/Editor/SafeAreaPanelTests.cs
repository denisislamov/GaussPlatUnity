using NUnit.Framework;
using UnityEngine;

namespace GSplat.Tests
{
    /// <summary>The one safe-area calculation the canvases and the OnGUI overlay share.</summary>
    public sealed class SafeAreaPanelTests
    {
        [Test]
        public void FullScreenSafeAreaMeansFullAnchors()
        {
            SafeAreaPanel.AnchorsFor(new Rect(0f, 0f, 1080f, 1920f), 1080, 1920, out Vector2 min, out Vector2 max);
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }

        [Test]
        public void NotchAtTheTopAndHomeBarAtTheBottomBecomeFractions()
        {
            // Screen.safeArea is y-up: a 100 px notch at the top and a 60 px bar at the bottom of a 1080x1920 screen.
            SafeAreaPanel.AnchorsFor(new Rect(0f, 60f, 1080f, 1760f), 1080, 1920, out Vector2 min, out Vector2 max);
            Assert.AreEqual(0f, min.x, 1e-6f);
            Assert.AreEqual(60f / 1920f, min.y, 1e-6f);
            Assert.AreEqual(1f, max.x, 1e-6f);
            Assert.AreEqual(1820f / 1920f, max.y, 1e-6f);
        }

        [Test]
        public void GuiRectFlipsYSoTheNotchEndsUpAtTheTop()
        {
            Rect gui = SafeAreaPanel.GuiRect(new Rect(0f, 60f, 1080f, 1760f), 1920);
            Assert.AreEqual(100f, gui.yMin, 1e-6f); // IMGUI y grows downward: the 100 px notch is the top margin
            Assert.AreEqual(1860f, gui.yMax, 1e-6f);
            Assert.AreEqual(0f, gui.xMin, 1e-6f);
            Assert.AreEqual(1080f, gui.width, 1e-6f);
        }
    }
}
