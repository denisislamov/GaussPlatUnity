using System;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace GSplat
{
    /// <summary>
    /// Runtime entry point: URL or bytes in, <see cref="GsplatData"/> out, with progress and typed errors.
    /// Decoding (pure C#, the slow part) runs on a worker thread where the platform has threads; the Burst
    /// build step has to run on the main thread because jobs can only be scheduled there.
    /// </summary>
    public static class SplatLoader
    {
        /// <summary>Downloads and builds. Use file:// URLs for local files. Must be awaited from the main thread.</summary>
        public static async Awaitable<GsplatData> LoadAsync(string url, SplatImportOptions options, IProgress<SplatLoadStatus> progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentException("The URL is empty.", nameof(url));
            if (options == null) throw new ArgumentNullException(nameof(options));

            byte[] bytes = await DownloadAsync(url, progress, cancellationToken);
            return await BuildAsync(bytes, options, progress, cancellationToken);
        }

        /// <summary>Decodes bytes already in memory (StreamingAssets, cache, tests) and builds GPU-ready data.</summary>
        public static async Awaitable<GsplatData> BuildAsync(byte[] bytes, SplatImportOptions options, IProgress<SplatLoadStatus> progress = null, CancellationToken cancellationToken = default)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (options == null) throw new ArgumentNullException(nameof(options));

            SplatFileKind kind = SplatFileKindDetector.Detect(bytes);
            if (kind == SplatFileKind.Unknown)
            {
                throw new SplatLoadException(SplatLoadError.UnsupportedFormat, "The file is not SPZ, PLY or .gsplat (unknown first bytes).");
            }

            progress?.Report(new SplatLoadStatus(SplatLoadStage.Decoding, 0f, "Decoding " + kind));
            if (kind == SplatFileKind.Gsplat)
            {
                // Already packed: nothing to build, the file is the GPU layout.
                GsplatData ready = Wrap(() => GsplatFile.Deserialize(bytes));
                progress?.Report(new SplatLoadStatus(SplatLoadStage.Ready, 1f, "Ready"));
                return ready;
            }

            SplatCloud cloud = await DecodeAsync(bytes, kind, cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new SplatLoadStatus(SplatLoadStage.Building, 0f, "Sorting and packing"));
                // TODO: Build runs Burst jobs synchronously on the main thread; ~100-300 ms for 500k splats. If that shows
                // as a hitch on phones, split it into per-stage awaits with Awaitable.NextFrameAsync between them.
                GsplatData data = Wrap(() => GsplatBuilder.Build(cloud, options));
                progress?.Report(new SplatLoadStatus(SplatLoadStage.Ready, 1f, "Ready"));
                return data;
            }
            finally
            {
                cloud.Dispose();
            }
        }

        private static async Awaitable<byte[]> DownloadAsync(string url, IProgress<SplatLoadStatus> progress, CancellationToken cancellationToken)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    progress?.Report(new SplatLoadStatus(SplatLoadStage.Downloading, request.downloadProgress, "Downloading"));
                    try
                    {
                        await Awaitable.NextFrameAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        request.Abort();
                        throw new SplatLoadException(SplatLoadError.Cancelled, "Loading was cancelled.");
                    }
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    return request.downloadHandler.data;
                }

                if (request.responseCode == 404)
                {
                    throw new SplatLoadException(SplatLoadError.NotFound, $"Nothing was found at {url} (HTTP 404).");
                }

                throw new SplatLoadException(SplatLoadError.Network, $"Could not download {url}: {request.error}");
            }
        }

        /// <summary>
        /// Header on the main thread (to size the cloud), body on a worker thread. Any exception is carried back to
        /// the main thread before it is rethrown, so the caller never continues on the worker.
        /// </summary>
        private static async Awaitable<SplatCloud> DecodeAsync(byte[] bytes, SplatFileKind kind, CancellationToken cancellationToken)
        {
            SplatCloud cloud;
            Action decode;
            switch (kind)
            {
                case SplatFileKind.Spz:
                {
                    SpzHeader header = Wrap(() => SpzReader.ReadHeader(bytes));
                    cloud = new SplatCloud(header.PointCount, Math.Min(header.ShDegree, ShMath.MaxDegree), header.Antialiased, Allocator.Persistent);
                    decode = () => SpzReader.Decode(bytes, header, cloud);
                    break;
                }
                case SplatFileKind.Ply:
                {
                    PlyHeader header = Wrap(() => PlyReader.ReadHeader(bytes));
                    cloud = new SplatCloud(header.VertexCount, PlyReader.ShDegreeOf(header), false, Allocator.Persistent);
                    decode = () => PlyReader.Decode(bytes, header, cloud);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Exception failure = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            // The web player has no worker threads unless the build enables Wasm threads; decode inline.
            // TODO: time-slice the decoders (a few thousand splats per frame) so a 500k file does not freeze the page.
            try { decode(); } catch (Exception e) { failure = e; }
#else
            await Awaitable.BackgroundThreadAsync();
            try
            {
                decode();
            }
            catch (Exception e)
            {
                failure = e;
            }

            await Awaitable.MainThreadAsync();
#endif
            if (failure != null)
            {
                cloud.Dispose();
                throw Translate(failure);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                cloud.Dispose();
                throw new SplatLoadException(SplatLoadError.Cancelled, "Loading was cancelled.");
            }

            return cloud;
        }

        private static T Wrap<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (Exception e) when (!(e is SplatLoadException))
            {
                throw Translate(e);
            }
        }

        /// <summary>Maps the format-specific exceptions onto the small set of errors the UI distinguishes.</summary>
        private static SplatLoadException Translate(Exception e)
        {
            switch (e)
            {
                case SplatLoadException already:
                    return already;
                case SpzException spz when spz.Code == SpzError.UnsupportedVersion || spz.Code == SpzError.UnsupportedCompression || spz.Code == SpzError.UnsupportedShDegree:
                    return new SplatLoadException(SplatLoadError.UnsupportedFormat, spz.Message, spz);
                case SpzException spz:
                    return new SplatLoadException(SplatLoadError.Corrupted, spz.Message, spz);
                case PlyException ply when ply.Code == PlyError.UnsupportedFormat || ply.Code == PlyError.UnsupportedPropertyType:
                    return new SplatLoadException(SplatLoadError.UnsupportedFormat, ply.Message, ply);
                case PlyException ply:
                    return new SplatLoadException(SplatLoadError.Corrupted, ply.Message, ply);
                case GsplatFileException gsplat when gsplat.Code == GsplatFileError.UnsupportedVersion:
                    return new SplatLoadException(SplatLoadError.UnsupportedFormat, gsplat.Message, gsplat);
                case GsplatFileException gsplat:
                    return new SplatLoadException(SplatLoadError.Corrupted, gsplat.Message, gsplat);
                case OutOfMemoryException oom:
                    return new SplatLoadException(SplatLoadError.OutOfMemory, "Not enough memory to decode this file.", oom);
                default:
                    return new SplatLoadException(SplatLoadError.Unknown, e.Message, e);
            }
        }
    }
}
