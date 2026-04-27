using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.Downloads.Editor.Tests
{
    /// <summary>
    /// Covers resumable UnityWebRequest download behavior using local HTTP responses instead of live remote services.
    /// </summary>
    internal sealed class DownloadUnityWebRequestResumableDownloaderTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.Downloads.UnityWebRequestTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        [Test]
        public async Task DownloadFileAsync_ResumesPartialFile_WhenServerSupportsRange()
        {
            var downloader = new UnityWebRequestResumableDownloader();
            var targetPath = Path.Combine(_rootDirectory, "model.onnx");
            var temporaryPath = targetPath + ".downloading";
            var fileBytes = Encoding.UTF8.GetBytes("abcdefghi");
            await File.WriteAllBytesAsync(temporaryPath, fileBytes[..3]);
            DownloadProgress? lastProgress = null;

            using var server = await LocalDownloadServer.StartAsync(fileBytes, respectRange: true);

            await downloader.DownloadFileAsync(
                server.Url,
                targetPath,
                fileBytes.Length,
                new ProgressRecorder(progress => lastProgress = progress),
                CancellationToken.None);

            Assert.That(await File.ReadAllBytesAsync(targetPath), Is.EqualTo(fileBytes));
            Assert.That(File.Exists(temporaryPath), Is.False);
            Assert.That(server.RequestedRanges, Is.EqualTo(new[] { "bytes=3-" }));
            Assert.That(lastProgress.HasValue, Is.True);
            Assert.That(lastProgress.Value.DownloadedBytes, Is.EqualTo(fileBytes.Length));
        }

        [Test]
        public async Task DownloadFileAsync_RestartsWhenServerIgnoresRange()
        {
            var downloader = new UnityWebRequestResumableDownloader();
            var targetPath = Path.Combine(_rootDirectory, "model.onnx");
            var temporaryPath = targetPath + ".downloading";
            var fileBytes = Encoding.UTF8.GetBytes("abcdefghi");
            await File.WriteAllBytesAsync(temporaryPath, fileBytes[..3]);

            using var server = await LocalDownloadServer.StartAsync(fileBytes, respectRange: false);

            await downloader.DownloadFileAsync(
                server.Url,
                targetPath,
                fileBytes.Length,
                null,
                CancellationToken.None);

            Assert.That(await File.ReadAllBytesAsync(targetPath), Is.EqualTo(fileBytes));
            Assert.That(File.Exists(temporaryPath), Is.False);
            Assert.That(server.RequestedRanges, Is.EqualTo(new[] { "bytes=3-", null }));
        }

        private sealed class ProgressRecorder : IProgress<DownloadProgress>
        {
            private readonly Action<DownloadProgress> _report;

            public ProgressRecorder(Action<DownloadProgress> report)
            {
                _report = report;
            }

            public void Report(DownloadProgress value)
            {
                _report?.Invoke(value);
            }
        }

        private sealed class LocalDownloadServer : IDisposable
        {
            private readonly byte[] _fileBytes;
            private readonly bool _respectRange;
            private readonly HttpListener _listener;
            private readonly string _url;
            private readonly CancellationTokenSource _cancellationTokenSource = new();
            private readonly Task _serverTask;

            private LocalDownloadServer(HttpListener listener, string url, byte[] fileBytes, bool respectRange)
            {
                _listener = listener;
                _url = url;
                _fileBytes = fileBytes;
                _respectRange = respectRange;
                _serverTask = Task.Run(RunAsync);
            }

            public string Url => _url;
            public List<string> RequestedRanges { get; } = new();

            public static Task<LocalDownloadServer> StartAsync(byte[] fileBytes, bool respectRange)
            {
                var port = GetFreePort();
                var prefix = $"http://127.0.0.1:{port}/";
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                listener.Start();
                return Task.FromResult(new LocalDownloadServer(listener, prefix, fileBytes, respectRange));
            }

            public void Dispose()
            {
                _cancellationTokenSource.Cancel();
                _listener.Stop();
                _listener.Close();
                try
                {
                    _serverTask.GetAwaiter().GetResult();
                }
                catch
                {
                    // Listener shutdown interrupts outstanding accepts. Test cleanup can ignore that noise.
                }

                _cancellationTokenSource.Dispose();
            }

            private async Task RunAsync()
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch when (_cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (HttpListenerException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    var rangeHeader = context.Request.Headers["Range"];
                    RequestedRanges.Add(rangeHeader);

                    if (_respectRange &&
                        !string.IsNullOrWhiteSpace(rangeHeader) &&
                        rangeHeader.StartsWith("bytes=", StringComparison.Ordinal) &&
                        int.TryParse(rangeHeader["bytes=".Length..^1], out var startOffset))
                    {
                        var remainingLength = _fileBytes.Length - startOffset;
                        context.Response.StatusCode = (int)HttpStatusCode.PartialContent;
                        context.Response.ContentLength64 = remainingLength;
                        context.Response.AddHeader("Content-Range", $"bytes {startOffset}-{_fileBytes.Length - 1}/{_fileBytes.Length}");
                        await context.Response.OutputStream.WriteAsync(_fileBytes, startOffset, remainingLength);
                    }
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        context.Response.ContentLength64 = _fileBytes.Length;
                        await context.Response.OutputStream.WriteAsync(_fileBytes, 0, _fileBytes.Length);
                    }

                    context.Response.OutputStream.Close();
                }
            }

            private static int GetFreePort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                try
                {
                    listener.Start();
                    return ((IPEndPoint)listener.LocalEndpoint).Port;
                }
                finally
                {
                    listener.Stop();
                }
            }
        }
    }
}
