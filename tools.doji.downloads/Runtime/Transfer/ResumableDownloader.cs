using System;
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
    /// Bytes go to <c>&lt;target&gt;.downloading</c>, and what the server said about the file goes to
    /// <c>&lt;target&gt;.downloading.json</c>. A later call continues the partial file with an HTTP <c>Range</c>
    /// request. The target is replaced only when the received size matches the size the server announced and the
    /// expected size. Callers that can run the same download concurrently must hold a <see cref="FileLock"/>.
    /// </remarks>
    public sealed class ResumableDownloader
    {
        internal const string PartialSuffix = ".downloading";
        internal const string PartialInfoSuffix = ".downloading.json";

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
        /// The size the caller expects, or null. A different size reported by the server fails the download before
        /// bytes are written.
        /// </param>
        /// <param name="progress">Receives bytes on disk and the best-known total. Reported on the calling thread.</param>
        /// <param name="cancellationToken">Cancels the request. The partial file stays for a later resume.</param>
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

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var partialPath = targetPath + PartialSuffix;
            var infoPath = targetPath + PartialInfoSuffix;

            // A partial file that cannot continue (range rejected, remote file changed) restarts from zero once.
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = LoadResumableInfo(partialPath, infoPath);
                var resumeSize = info != null ? new FileInfo(partialPath).Length : 0L;

                if (info != null && IsComplete(resumeSize, info.TotalBytes, expectedSizeInBytes))
                {
                    progress?.Report(new DownloadProgress(resumeSize, resumeSize));
                    break;
                }

                var (rejection, message) = await DownloadAttemptAsync(
                    url,
                    partialPath,
                    infoPath,
                    info,
                    resumeSize,
                    expectedSizeInBytes,
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

        private static async Task<(Rejection Rejection, string Message)> DownloadAttemptAsync(
            string url,
            string partialPath,
            string infoPath,
            PartialDownloadInfo info,
            long resumeSize,
            long? expectedSizeInBytes,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var handler = new PartialFileHandler(partialPath, infoPath, info, resumeSize, expectedSizeInBytes, progress);
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, handler, null);
            handler.Request = request;

            // Compressed responses do not match byte ranges or Content-Length.
            request.SetRequestHeader("Accept-Encoding", "identity");
            if (resumeSize > 0)
            {
                request.SetRequestHeader("Range", $"bytes={resumeSize}-");
                var validator = info?.RangeValidator;
                if (validator != null)
                {
                    request.SetRequestHeader("If-Range", validator);
                }
            }

            try
            {
                try
                {
                    await request.SendAsync(cancellationToken);
                }
                catch (WebRequestException) when (handler.Rejection != Rejection.None)
                {
                    // The handler aborted the transfer on purpose.
                    return (handler.Rejection, handler.RejectionMessage);
                }
                catch (WebRequestException exception) when (exception.ResponseCode == 416 && resumeSize > 0)
                {
                    return (Rejection.RestartFromZero, exception.Message);
                }

                // An empty body never reaches the handler's data callback.
                handler.EnsureAccepted();
                return (handler.Rejection, handler.RejectionMessage);
            }
            finally
            {
                request.Dispose();
                handler.CloseFile();
            }
        }

        /// <summary>
        /// Returns the info for a partial file that can continue, or null after removing leftovers that cannot.
        /// </summary>
        private static PartialDownloadInfo LoadResumableInfo(string partialPath, string infoPath)
        {
            if (!File.Exists(partialPath))
            {
                DeleteIfExists(infoPath);
                return null;
            }

            var info = JsonStateStore.Load<PartialDownloadInfo>(infoPath);
            if (info == null)
            {
                // Without the server's validator, the bytes may belong to another version of the file.
                DiscardPartial(partialPath, infoPath);
            }

            return info;
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

        /// <summary>
        /// Decides from the response headers whether the body continues the partial file, replaces it, or is rejected,
        /// and only then opens the file. Error bodies are never written.
        /// </summary>
        private sealed class PartialFileHandler : DownloadHandlerScript
        {
            private readonly string _partialPath;
            private readonly string _infoPath;
            private readonly PartialDownloadInfo _info;
            private readonly long _resumeSize;
            private readonly long? _expectedSizeInBytes;
            private readonly IProgress<DownloadProgress> _progress;
            private FileStream _fileStream;
            private bool _accepted;
            private long _downloadedBytes;
            private long? _totalBytes;

            public PartialFileHandler(
                string partialPath,
                string infoPath,
                PartialDownloadInfo info,
                long resumeSize,
                long? expectedSizeInBytes,
                IProgress<DownloadProgress> progress)
                : base(new byte[64 * 1024])
            {
                _partialPath = partialPath;
                _infoPath = infoPath;
                _info = info;
                _resumeSize = resumeSize;
                _expectedSizeInBytes = expectedSizeInBytes;
                _progress = progress;
            }

            public UnityWebRequest Request { private get; set; }
            public Rejection Rejection { get; private set; }
            public string RejectionMessage { get; private set; }

            /// <summary>
            /// Accepts a 2xx response once its headers are known. Returns false when the transfer must stop.
            /// </summary>
            public bool EnsureAccepted()
            {
                if (Rejection != Rejection.None)
                {
                    return false;
                }

                if (_accepted)
                {
                    return true;
                }

                var responseCode = Request.responseCode;
                if (responseCode < 200 || responseCode >= 300)
                {
                    return true;
                }

                _accepted = true;
                var contentLength = ParseLong(Request.GetResponseHeader("Content-Length"));
                if (responseCode == 206)
                {
                    if (!TryParseContentRange(Request.GetResponseHeader("Content-Range"), out var start, out var total) ||
                        start != _resumeSize)
                    {
                        return Reject(Rejection.RestartFromZero, "the server returned a different byte range.");
                    }

                    if (total.HasValue && _info?.TotalBytes.HasValue == true && total.Value != _info.TotalBytes.Value)
                    {
                        return Reject(Rejection.RestartFromZero, "the remote file changed size.");
                    }

                    _totalBytes = total ?? _info?.TotalBytes ?? (contentLength.HasValue ? _resumeSize + contentLength : null);
                    if (!CheckExpectedSize())
                    {
                        return false;
                    }

                    _fileStream = new FileStream(_partialPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    _downloadedBytes = _resumeSize;
                    if (_info != null && !_info.TotalBytes.HasValue && _totalBytes.HasValue)
                    {
                        _info.TotalBytes = _totalBytes;
                        JsonStateStore.Save(_infoPath, _info);
                    }
                }
                else
                {
                    // The full body: a fresh download, a server that ignores Range, or a changed file (If-Range).
                    _totalBytes = contentLength;
                    if (!CheckExpectedSize())
                    {
                        return false;
                    }

                    // Truncate before saving the new validator, so old bytes never pair with new info.
                    _fileStream = new FileStream(_partialPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    _downloadedBytes = 0;
                    JsonStateStore.Save(_infoPath, new PartialDownloadInfo
                    {
                        ETag = Request.GetResponseHeader("ETag"),
                        LastModified = Request.GetResponseHeader("Last-Modified"),
                        TotalBytes = _totalBytes,
                    });
                }

                _progress?.Report(new DownloadProgress(_downloadedBytes, _totalBytes));
                return true;
            }

            public void CloseFile()
            {
                _fileStream?.Dispose();
                _fileStream = null;
            }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                EnsureAccepted();
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (!EnsureAccepted())
                {
                    return false;
                }

                if (_fileStream == null || data == null || dataLength <= 0)
                {
                    return true;
                }

                _fileStream.Write(data, 0, dataLength);
                _downloadedBytes += dataLength;
                _progress?.Report(new DownloadProgress(_downloadedBytes, _totalBytes));
                return true;
            }

            protected override void CompleteContent()
            {
                _fileStream?.Flush();
            }

            private bool CheckExpectedSize()
            {
                if (_expectedSizeInBytes.HasValue && _totalBytes.HasValue && _expectedSizeInBytes.Value != _totalBytes.Value)
                {
                    return Reject(
                        Rejection.SizeMismatch,
                        $"The server reports {_totalBytes.Value} bytes for '{Request.url}', but {_expectedSizeInBytes.Value} were expected.");
                }

                return true;
            }

            private bool Reject(Rejection rejection, string message)
            {
                Rejection = rejection;
                RejectionMessage = message;
                return false;
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
        }
    }
}
