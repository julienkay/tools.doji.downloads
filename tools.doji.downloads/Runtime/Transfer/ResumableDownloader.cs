using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Doji.Downloads
{
    /// <summary>
    /// Downloads one URL to a file and continues an interrupted download where it stopped.
    /// </summary>
    /// <remarks>
    /// Bytes collect in <c>&lt;target&gt;.downloading</c>, and what the server said about the file goes to
    /// <c>&lt;target&gt;.downloading.json</c>. A later call for the same URL continues the partial file with an HTTP
    /// <c>Range</c> request, but only when the server gave a validator (a strong <c>ETag</c> or <c>Last-Modified</c>);
    /// otherwise it starts over. Each request writes its body to <c>&lt;target&gt;.downloading.segment</c> with Unity's
    /// <see cref="DownloadHandlerFile"/>, so the bytes never pass through managed code. The segment joins the partial
    /// file only after the response is checked. The target is replaced only when the received size matches the size
    /// the server announced and the expected size. Callers that can run the same download concurrently must hold a
    /// <see cref="FileLock"/>.
    /// </remarks>
    public sealed class ResumableDownloader
    {
        internal const string PartialSuffix = ".downloading";
        internal const string PartialInfoSuffix = ".downloading.json";
        internal const string SegmentSuffix = ".downloading.segment";
        private const long ProgressIntervalMilliseconds = 100;

        private enum Rejection
        {
            None,
            RestartFromZero,
            SizeMismatch,
        }

        /// <summary>
        /// Downloads <paramref name="url"/> to <paramref name="targetPath"/>, continuing a partial download if one exists.
        /// </summary>
        /// <param name="expectedSizeInBytes">
        /// The size the caller expects, or null. A different size reported by the server fails the download as soon as
        /// the response headers arrive, without changing the partial file.
        /// </param>
        /// <param name="progress">
        /// Receives bytes on disk and the best-known total, at most every 100 ms plus once at the start and the end.
        /// Reported on the calling thread.
        /// </param>
        /// <param name="cancellationToken">Cancels the request. The bytes received so far stay for a later resume.</param>
        /// <exception cref="WebRequestException">The server or the connection failed. The partial file stays.</exception>
        /// <exception cref="IOException">The download is incomplete or has the wrong size. The target is not changed.</exception>
        public async Task DownloadFileAsync(
            string url,
            string targetPath,
            long? expectedSizeInBytes,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL is required.", nameof(url));
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Target path is required.", nameof(targetPath));
            }

            // A bare file name has no directory part; resolve it against the current directory.
            targetPath = Path.GetFullPath(targetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            var partialPath = targetPath + PartialSuffix;
            var infoPath = targetPath + PartialInfoSuffix;
            var segmentPath = targetPath + SegmentSuffix;

            // A partial file that cannot continue (range rejected, remote file changed) restarts from zero once.
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = LoadResumableInfo(url, partialPath, infoPath, expectedSizeInBytes);
                var resumeSize = info != null ? new FileInfo(partialPath).Length : 0L;

                if (info != null && IsComplete(resumeSize, info.TotalBytes, expectedSizeInBytes))
                {
                    progress?.Report(new DownloadProgress(resumeSize, resumeSize));
                    break;
                }

                var (rejection, message) = await DownloadAttemptAsync(
                    new Attempt(url, partialPath, infoPath, segmentPath, info, resumeSize, expectedSizeInBytes),
                    progress,
                    cancellationToken);

                if (rejection == Rejection.None)
                {
                    break;
                }

                DiscardPartial(partialPath, infoPath);
                if (rejection == Rejection.SizeMismatch)
                {
                    throw new IOException(message);
                }

                if (attempt > 0)
                {
                    throw new IOException($"Download of '{url}' could not continue: {message}");
                }
            }

            VerifyComplete(url, partialPath, infoPath, expectedSizeInBytes);
            ReplaceTarget(partialPath, targetPath);
            DeleteIfExists(infoPath);
        }

        /// <summary>
        /// Sends one request into a fresh segment file, then decides from the response whether the segment continues
        /// the partial file, replaces it, or is dropped. Bytes that arrived before a dropped connection or a
        /// cancellation are kept when the response was accepted, so the next call resumes after them.
        /// </summary>
        private static async Task<(Rejection Rejection, string Message)> DownloadAttemptAsync(
            Attempt attempt,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            DeleteIfExists(attempt.SegmentPath);
            var request = new UnityWebRequest(
                attempt.Url,
                UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerFile(attempt.SegmentPath) { removeFileOnAbort = false },
                null);

            // Compressed responses do not match byte ranges or Content-Length.
            request.SetRequestHeader("Accept-Encoding", "identity");
            if (attempt.ResumeSize > 0)
            {
                request.SetRequestHeader("Range", $"bytes={attempt.ResumeSize}-");
                var validator = attempt.Info?.RangeValidator;
                if (validator != null)
                {
                    request.SetRequestHeader("If-Range", validator);
                }
            }

            Response response = null;
            var canceled = false;
            var sinceProgress = Stopwatch.StartNew();
            try
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        canceled = true;
                        request.Abort();
                        break;
                    }

                    // Check the headers as soon as they arrive, so a wrong file is not downloaded to the end.
                    // A redirect status shows while Unity follows the redirect; wait for the final response.
                    if (response == null && request.responseCode != 0 && !IsRedirect(request.responseCode))
                    {
                        response = Response.Read(request, attempt);
                        if (response.Rejection != Rejection.None)
                        {
                            request.Abort();
                            break;
                        }

                        progress?.Report(response.Progress(request));
                        sinceProgress.Restart();
                    }
                    else if (response != null && sinceProgress.ElapsedMilliseconds >= ProgressIntervalMilliseconds)
                    {
                        progress?.Report(response.Progress(request));
                        sinceProgress.Restart();
                    }

                    await Task.Yield();
                }

                // A fast response can finish before the loop saw its headers.
                if (response == null && request.responseCode != 0)
                {
                    response = Response.Read(request, attempt);
                }

                var finalProgress = response?.Progress(request);
                var result = request.result;
                var error = request.error;
                var responseCode = request.responseCode;
                request.Dispose();
                request = null;

                if (response?.Rejection is Rejection.RestartFromZero or Rejection.SizeMismatch)
                {
                    return (response.Rejection, response.RejectionMessage);
                }

                if (response is { IsSuccess: true })
                {
                    // Bytes of an accepted response are valid, even when the connection dropped after them.
                    response.MergeSegment(attempt);
                    if (finalProgress.HasValue)
                    {
                        progress?.Report(finalProgress.Value);
                    }
                }

                if (canceled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (responseCode == 416 && attempt.ResumeSize > 0)
                {
                    return (Rejection.RestartFromZero, $"the server rejected the range ({error}).");
                }

                if (result != UnityWebRequest.Result.Success)
                {
                    throw new WebRequestException(attempt.Url, responseCode, error);
                }

                return (Rejection.None, null);
            }
            finally
            {
                request?.Dispose();
                DeleteIfExists(attempt.SegmentPath);
            }
        }

        /// <summary>
        /// Returns the info for a partial file that can continue or is complete, or null after removing leftovers
        /// that cannot.
        /// </summary>
        /// <remarks>
        /// A partial file is kept only when it came from the same URL. An incomplete one also needs a validator:
        /// without it, a changed remote file of the same size would be appended to bytes of the old version.
        /// </remarks>
        private static PartialDownloadInfo LoadResumableInfo(
            string url,
            string partialPath,
            string infoPath,
            long? expectedSizeInBytes)
        {
            if (!File.Exists(partialPath))
            {
                DeleteIfExists(infoPath);
                return null;
            }

            var info = JsonStateStore.Load<PartialDownloadInfo>(infoPath);
            if (info == null || !string.Equals(info.Url, url, StringComparison.Ordinal))
            {
                DiscardPartial(partialPath, infoPath);
                return null;
            }

            var size = new FileInfo(partialPath).Length;
            if (!IsComplete(size, info.TotalBytes, expectedSizeInBytes) && info.RangeValidator == null)
            {
                DiscardPartial(partialPath, infoPath);
                return null;
            }

            return info;
        }

        private static bool IsRedirect(long responseCode)
        {
            return responseCode >= 300 && responseCode < 400;
        }

        private static bool IsComplete(long size, long? totalBytes, long? expectedSizeInBytes)
        {
            return size > 0 &&
                   totalBytes == size &&
                   (!expectedSizeInBytes.HasValue || expectedSizeInBytes.Value == size);
        }

        private static void VerifyComplete(string url, string partialPath, string infoPath, long? expectedSizeInBytes)
        {
            var size = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;
            var totalBytes = JsonStateStore.Load<PartialDownloadInfo>(infoPath)?.TotalBytes ?? expectedSizeInBytes;
            if (!totalBytes.HasValue || size == totalBytes.Value)
            {
                if (!File.Exists(partialPath))
                {
                    // An empty body never creates a file.
                    File.WriteAllBytes(partialPath, Array.Empty<byte>());
                }

                return;
            }

            if (size < totalBytes.Value)
            {
                // The bytes so far are a valid prefix: keep them so the next call resumes.
                throw new IOException(
                    $"Download of '{url}' ended early: received {size} of {totalBytes.Value} bytes. Download again to resume.");
            }

            DiscardPartial(partialPath, infoPath);
            throw new IOException($"Download of '{url}' received {size} bytes, but {totalBytes.Value} were expected.");
        }

        private static void ReplaceTarget(string partialPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                File.Replace(partialPath, targetPath, null);
            }
            else
            {
                File.Move(partialPath, targetPath);
            }
        }

        private static void DiscardPartial(string partialPath, string infoPath)
        {
            DeleteIfExists(partialPath);
            DeleteIfExists(infoPath);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static long? ParseLong(string value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }

        /// <summary>
        /// Parses <c>bytes start-end/total</c>, where total may be <c>*</c>.
        /// </summary>
        private static bool TryParseContentRange(string value, out long start, out long? total)
        {
            start = 0;
            total = null;
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var range = value.Substring("bytes ".Length).Trim();
            var slash = range.IndexOf('/');
            var dash = range.IndexOf('-');
            if (slash < 0 || dash < 0 || dash > slash)
            {
                return false;
            }

            if (!long.TryParse(range.Substring(0, dash), NumberStyles.Integer, CultureInfo.InvariantCulture, out start))
            {
                return false;
            }

            total = ParseLong(range.Substring(slash + 1));
            return true;
        }

        /// <summary>
        /// What one request is for: the URL, the files it writes, and the partial file it may continue.
        /// </summary>
        private sealed class Attempt
        {
            public Attempt(
                string url,
                string partialPath,
                string infoPath,
                string segmentPath,
                PartialDownloadInfo info,
                long resumeSize,
                long? expectedSizeInBytes)
            {
                Url = url;
                PartialPath = partialPath;
                InfoPath = infoPath;
                SegmentPath = segmentPath;
                Info = info;
                ResumeSize = resumeSize;
                ExpectedSizeInBytes = expectedSizeInBytes;
            }

            public string Url { get; }
            public string PartialPath { get; }
            public string InfoPath { get; }
            public string SegmentPath { get; }
            public PartialDownloadInfo Info { get; }
            public long ResumeSize { get; }
            public long? ExpectedSizeInBytes { get; }
        }

        /// <summary>
        /// The decision on one response, made from its status and headers: continue the partial file, replace it, or
        /// reject the response.
        /// </summary>
        private sealed class Response
        {
            private PartialDownloadInfo _info;

            private Response()
            {
            }

            public bool IsSuccess { get; private set; }

            /// <summary>
            /// Whether the body continues the partial file (a <c>206</c> at the partial size) instead of replacing it.
            /// </summary>
            public bool Appends { get; private set; }

            public long StartOffset { get; private set; }
            public long? TotalBytes { get; private set; }
            public Rejection Rejection { get; private set; }
            public string RejectionMessage { get; private set; }

            public static Response Read(UnityWebRequest request, Attempt attempt)
            {
                var response = new Response();
                var responseCode = request.responseCode;
                if (responseCode < 200 || responseCode >= 300)
                {
                    return response;
                }

                response.IsSuccess = true;
                var contentLength = ParseLong(request.GetResponseHeader("Content-Length"));
                if (responseCode == 206)
                {
                    if (!TryParseContentRange(request.GetResponseHeader("Content-Range"), out var start, out var total))
                    {
                        return response.Reject(Rejection.RestartFromZero, "the server returned a different byte range.");
                    }

                    if (attempt.ResumeSize > 0)
                    {
                        if (start != attempt.ResumeSize)
                        {
                            return response.Reject(Rejection.RestartFromZero, "the server returned a different byte range.");
                        }

                        if (total.HasValue && attempt.Info?.TotalBytes.HasValue == true && total.Value != attempt.Info.TotalBytes.Value)
                        {
                            return response.Reject(Rejection.RestartFromZero, "the remote file changed size.");
                        }

                        response.Appends = true;
                        response.StartOffset = attempt.ResumeSize;
                        response.TotalBytes = total ?? attempt.Info?.TotalBytes ?? (contentLength.HasValue ? attempt.ResumeSize + contentLength : null);
                        response._info = attempt.Info;
                    }
                    else
                    {
                        // Without a resume there is no partial file to continue; an unsolicited 206 starts a new one.
                        // Keep the announced total, so the final size check catches a body that covers only part of the file.
                        response.TotalBytes = total ?? contentLength;
                        response._info = CreateInfo(request, attempt.Url);
                    }
                }
                else
                {
                    // The full body: a fresh download, a server that ignores Range, or a changed file (If-Range).
                    response.TotalBytes = contentLength;
                    response._info = CreateInfo(request, attempt.Url);
                }

                if (attempt.ExpectedSizeInBytes.HasValue && response.TotalBytes.HasValue && attempt.ExpectedSizeInBytes.Value != response.TotalBytes.Value)
                {
                    return response.Reject(
                        Rejection.SizeMismatch,
                        $"The server reports {response.TotalBytes.Value} bytes for '{attempt.Url}', but {attempt.ExpectedSizeInBytes.Value} were expected.");
                }

                return response;
            }

            public DownloadProgress Progress(UnityWebRequest request)
            {
                return new DownloadProgress(StartOffset + (long)request.downloadedBytes, TotalBytes);
            }

            /// <summary>
            /// Appends the segment to the partial file, or moves it into its place, then saves the info that describes
            /// the result. The info is saved after the bytes, so old bytes never pair with a new validator.
            /// </summary>
            public void MergeSegment(Attempt attempt)
            {
                var hasSegment = File.Exists(attempt.SegmentPath);
                if (Appends)
                {
                    if (hasSegment)
                    {
                        using var source = new FileStream(attempt.SegmentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
                        using var destination = new FileStream(attempt.PartialPath, FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024);
                        source.CopyTo(destination, 1024 * 1024);
                    }
                }
                else
                {
                    // Remove the info first: an interruption here leaves no info, and a partial file without info is
                    // discarded instead of resumed with the wrong validator.
                    DeleteIfExists(attempt.InfoPath);
                    DeleteIfExists(attempt.PartialPath);
                    if (hasSegment)
                    {
                        File.Move(attempt.SegmentPath, attempt.PartialPath);
                    }
                }

                if (_info != null && (!Appends || _info.TotalBytes != TotalBytes))
                {
                    _info.TotalBytes = TotalBytes;
                    JsonStateStore.Save(attempt.InfoPath, _info);
                }
            }

            private static PartialDownloadInfo CreateInfo(UnityWebRequest request, string url)
            {
                return new PartialDownloadInfo
                {
                    Url = url,
                    ETag = request.GetResponseHeader("ETag"),
                    LastModified = request.GetResponseHeader("Last-Modified"),
                };
            }

            private Response Reject(Rejection rejection, string message)
            {
                Rejection = rejection;
                RejectionMessage = message;
                return this;
            }
        }
    }
}
