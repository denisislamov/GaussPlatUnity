using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    public sealed class SplatLoaderTests
    {
        private sealed class StatusCollector : IProgress<SplatLoadStatus>
        {
            public readonly List<SplatLoadStatus> Reports = new List<SplatLoadStatus>();

            public void Report(SplatLoadStatus value)
            {
                Reports.Add(value);
            }
        }

        private static IEnumerator Await<T>(Awaitable<T> awaitable, Action<T> onResult, Action<Exception> onError)
        {
            bool done = false;
            Run();
            while (!done) yield return null;

            async void Run()
            {
                try
                {
                    onResult(await awaitable);
                }
                catch (Exception e)
                {
                    onError(e);
                }
                finally
                {
                    done = true;
                }
            }
        }

        [UnityTest]
        public IEnumerator BuildsFromSpzBytesAndReportsStages()
        {
            byte[] bytes;
            using (SplatCloud cloud = TestCloudsRuntime.Random(2000, 1))
            {
                bytes = SpzWriter.Write(cloud);
            }

            var collector = new StatusCollector();
            GsplatData result = null;
            Exception failure = null;
            var options = new SplatImportOptions { TargetShDegree = 1 };
            yield return Await(SplatLoader.BuildAsync(bytes, options, collector), data => result = data, e => failure = e);

            Assert.IsNull(failure, failure?.ToString());
            using (result)
            {
                Assert.AreEqual(2000, result.SplatCount);
                Assert.AreEqual(1, result.ShDegree);
            }

            Assert.That(collector.Reports.Count, Is.GreaterThanOrEqualTo(3));
            Assert.AreEqual(SplatLoadStage.Decoding, collector.Reports[0].Stage);
            Assert.AreEqual(SplatLoadStage.Ready, collector.Reports[collector.Reports.Count - 1].Stage);
        }

        [UnityTest]
        public IEnumerator LoadsFromAFileUrl()
        {
            string path = Path.Combine(Application.temporaryCachePath, "gsplat-loader-test.spz");
            using (SplatCloud cloud = TestCloudsRuntime.Random(500, 0))
            {
                File.WriteAllBytes(path, SpzWriter.Write(cloud));
            }

            GsplatData result = null;
            Exception failure = null;
            yield return Await(SplatLoader.LoadAsync(new Uri(path).AbsoluteUri, new SplatImportOptions()), data => result = data, e => failure = e);
            File.Delete(path);

            Assert.IsNull(failure, failure?.ToString());
            using (result)
            {
                Assert.AreEqual(500, result.SplatCount);
            }
        }

        [UnityTest]
        public IEnumerator UnknownBytesAreUnsupportedFormat()
        {
            Exception failure = null;
            yield return Await(SplatLoader.BuildAsync(new byte[] { 9, 9, 9, 9, 9, 9 }, new SplatImportOptions()), data => data.Dispose(), e => failure = e);

            var loadFailure = failure as SplatLoadException;
            Assert.IsNotNull(loadFailure, "expected SplatLoadException, got " + failure);
            Assert.AreEqual(SplatLoadError.UnsupportedFormat, loadFailure.Code);
        }

        [UnityTest]
        public IEnumerator CorruptedSpzIsCorrupted()
        {
            byte[] bytes;
            using (SplatCloud cloud = TestCloudsRuntime.Random(100, 0))
            {
                bytes = SpzWriter.Write(cloud);
            }

            for (int byteIndex = SpzHeader.LegacyHeaderSize; byteIndex < bytes.Length; byteIndex++) bytes[byteIndex] = 0x55;

            Exception failure = null;
            yield return Await(SplatLoader.BuildAsync(bytes, new SplatImportOptions()), data => data.Dispose(), e => failure = e);

            var loadFailure = failure as SplatLoadException;
            Assert.IsNotNull(loadFailure, "expected SplatLoadException, got " + failure);
            Assert.AreEqual(SplatLoadError.Corrupted, loadFailure.Code);
        }

        [UnityTest]
        public IEnumerator MissingFileIsNotFoundOrNetwork()
        {
            Exception failure = null;
            string url = new Uri(Path.Combine(Application.temporaryCachePath, "does-not-exist.spz")).AbsoluteUri;
            yield return Await(SplatLoader.LoadAsync(url, new SplatImportOptions()), data => data.Dispose(), e => failure = e);

            var loadFailure = failure as SplatLoadException;
            Assert.IsNotNull(loadFailure, "expected SplatLoadException, got " + failure);
            Assert.That(loadFailure.Code, Is.EqualTo(SplatLoadError.NotFound).Or.EqualTo(SplatLoadError.Network));
        }
    }
}
