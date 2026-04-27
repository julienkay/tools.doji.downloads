using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Doji.Downloads.Editor
{
    /// <summary>
    /// Streams URL downloads to disk with sibling partial files and HTTP Range resume support.
    /// </summary>
    public sealed class UnityWebRequestResumableDownloader
    {
        /// <summary>
        /// Downloads one URL into the target path, resuming from `<target>.downloading` when possible.
        /// </summary>
        public async Task DownloadFileAsync(
            string url,
            string targetPath,
            long? expectedSizeInBytes,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var temporaryPath = targetPath + ".downloading";
            var resumeSize = File.Exists(temporaryPath)
                ? new FileInfo(temporaryPath).Length
                : 0L;

            var responseCode = await DownloadAttemptAsync(
                url,
                temporaryPath,
                resumeSize,
                expectedSizeInBytes,
                progress,
                cancellationToken);

            // If server ignored Range, restart once from zero to avoid duplicate appended bytes.
            if (resumeSize > 0 && responseCode == 200)
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                await DownloadAttemptAsync(
                    url,
                    temporaryPath,
                    0,
                    expectedSizeInBytes,
                    progress,
                    cancellationToken);
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(temporaryPath, targetPath);
        }

        internal static async Task<long> DownloadAttemptAsync(
            string url,
            string temporaryPath,
            long resumeSize,
            long? expectedSizeInBytes,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Get(url);
            if (resumeSize > 0)
            {
                request.SetRequestHeader("Range", $"bytes={resumeSize}-");
            }

            using var fileStream = new FileStream(
                temporaryPath,
                resumeSize > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            request.downloadHandler = new ResumableDownloadHandler(fileStream, resumeSize, expectedSizeInBytes, progress);

            await SendAsync(request, cancellationToken);
            return request.responseCode;
        }

        /// <summary>
        /// Awaits a UnityWebRequest without blocking the editor thread.
        /// </summary>
        public static async Task SendAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException($"Request failed ({request.responseCode}): {request.error}");
            }
        }

        private sealed class ResumableDownloadHandler : DownloadHandlerScript
        {
            private readonly FileStream _fileStream;
            private readonly IProgress<DownloadProgress> _progress;
            private long _downloadedBytes;
            private long? _totalBytes;

            public ResumableDownloadHandler(
                FileStream fileStream,
                long resumeSize,
                long? expectedSizeInBytes,
                IProgress<DownloadProgress> progress)
                : base(new byte[64 * 1024])
            {
                _fileStream = fileStream;
                _downloadedBytes = resumeSize;
                _totalBytes = expectedSizeInBytes;
                _progress = progress;
                _progress?.Report(new DownloadProgress(_downloadedBytes, _totalBytes));
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                {
                    return true;
                }

                _fileStream.Write(data, 0, dataLength);
                _downloadedBytes += dataLength;
                _progress?.Report(new DownloadProgress(_downloadedBytes, _totalBytes));
                return true;
            }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                if (contentLength > 0)
                {
                    _totalBytes = Math.Max(_downloadedBytes, (long)contentLength + _downloadedBytes);
                    _progress?.Report(new DownloadProgress(_downloadedBytes, _totalBytes));
                }
            }

            protected override void CompleteContent()
            {
                _fileStream.Flush();
                _progress?.Report(new DownloadProgress(_downloadedBytes, _totalBytes));
            }
        }
    }
}
