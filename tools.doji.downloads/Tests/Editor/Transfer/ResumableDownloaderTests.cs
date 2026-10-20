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
    /// Covers resume, restart and failure behavior of <see cref="ResumableDownloader"/> against a local HTTP server.
    /// Every failure case asserts that the target path is not touched.
    /// </summary>
    internal sealed class ResumableDownloaderTests
    {
        private static readonly byte[] FileBytes = Encoding.UTF8.GetBytes("abcdefghi");
        private string _rootDirectory;
        private string _targetPath;

        private string PartialPath => _targetPath + ResumableDownloader.PartialSuffix;
        private string InfoPath => _targetPath + ResumableDownloader.PartialInfoSuffix;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.Downloads.DownloaderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _targetPath = Path.Combine(_rootDirectory, "model.onnx");
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
        public async Task DownloadFileAsync_DownloadsFileAndRemovesPartialFiles()
        {
            using var server = TestServer.Start(FileBytes);

            await new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(File.Exists(PartialPath), Is.False);
            Assert.That(File.Exists(InfoPath), Is.False);
            Assert.That(server.Requests, Is.EqualTo(new[] { (default(string), default(string)) }));
        }

        [Test]
        public async Task DownloadFileAsync_ResumesAfterInterruptedTransfer()
        {
            using var server = TestServer.Start(FileBytes);
            server.ETag = "\"v1\"";
            server.CutFirstResponseAfterBytes = 4;
            var downloader = new ResumableDownloader();

            await CatchAsync<WebRequestException>(() =>
                downloader.DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None));

            Assert.That(File.Exists(_targetPath), Is.False);
            Assert.That(File.Exists(InfoPath), Is.True);
            var partialLength = new FileInfo(PartialPath).Length;
            Assert.That(partialLength, Is.InRange(1, FileBytes.Length - 1));

            DownloadProgress? lastProgress = null;
            await downloader.DownloadFileAsync(
                server.Url,
                _targetPath,
                FileBytes.Length,
                new SyncProgress(progress => lastProgress = progress),
                CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(server.Requests[1], Is.EqualTo(($"bytes={partialLength}-", "\"v1\"")));
            Assert.That(lastProgress?.DownloadedBytes, Is.EqualTo(FileBytes.Length));
            Assert.That(lastProgress?.TotalBytes, Is.EqualTo(FileBytes.Length));
        }

        [Test]
        public async Task DownloadFileAsync_ReportsProgressFewerTimesThanChunks()
        {
            var largeFile = new byte[8 * 1024 * 1024];
            new Random(1).NextBytes(largeFile);
            using var server = TestServer.Start(largeFile);
            var reports = new List<DownloadProgress>();

            await new ResumableDownloader().DownloadFileAsync(
                server.Url,
                _targetPath,
                largeFile.Length,
                new SyncProgress(reports.Add),
                CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(largeFile));
            Assert.That(reports.Count, Is.LessThan(largeFile.Length / (64 * 1024)));
            Assert.That(reports[reports.Count - 1].DownloadedBytes, Is.EqualTo(largeFile.Length));
            Assert.That(reports[reports.Count - 1].TotalBytes, Is.EqualTo(largeFile.Length));
        }

        [Test]
        public async Task DownloadFileAsync_ReplacesPartialInOneRequest_WhenServerIgnoresRange()
        {
            using var server = TestServer.Start(FileBytes);
            server.RespectRange = false;
            WritePartial(server.Url, "abc", new PartialDownloadInfo { ETag = "\"v1\"", TotalBytes = FileBytes.Length });

            await new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(server.Requests, Is.EqualTo(new[] { ("bytes=3-", "\"v1\"") }));
        }

        [Test]
        public async Task DownloadFileAsync_RestartsWhenRemoteFileChanged()
        {
            using var server = TestServer.Start(FileBytes);
            server.ETag = "\"new\"";
            WritePartial(server.Url, "XYZ", new PartialDownloadInfo { ETag = "\"old\"", TotalBytes = FileBytes.Length });

            await new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(server.Requests, Is.EqualTo(new[] { ("bytes=3-", "\"old\"") }));
        }

        [Test]
        public async Task DownloadFileAsync_RestartsFromZero_WhenServerRejectsRange()
        {
            using var server = TestServer.Start(FileBytes);
            server.ETag = "\"v1\"";
            WritePartial(server.Url, "abcdefghijkl", new PartialDownloadInfo { ETag = "\"v1\"" });

            await new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(server.Requests, Is.EqualTo(new[] { ("bytes=12-", "\"v1\""), (default(string), default(string)) }));
        }

        [Test]
        public async Task DownloadFileAsync_FinishesCompletePartialWithoutRequest()
        {
            using var server = TestServer.Start(FileBytes);
            WritePartial(server.Url, "abcdefghi", new PartialDownloadInfo { TotalBytes = FileBytes.Length });

            await new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(server.Requests, Is.Empty);
        }

        [Test]
        public async Task DownloadFileAsync_DiscardsPartialWithoutInfo()
        {
            using var server = TestServer.Start(FileBytes);
            File.WriteAllText(PartialPath, "zzz");

            await new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(server.Requests, Is.EqualTo(new[] { (default(string), default(string)) }));
        }

        [Test]
        public async Task DownloadFileAsync_RestartsIncompletePartialWithoutValidator()
        {
            // Same size, other content: without a validator the server cannot tell the file changed.
            using var server = TestServer.Start(FileBytes);
            WritePartial(server.Url, "XYZ", new PartialDownloadInfo { TotalBytes = FileBytes.Length });

            await new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(server.Requests, Is.EqualTo(new[] { (default(string), default(string)) }));
        }

        [Test]
        public async Task DownloadFileAsync_RestartsPartialFromAnotherUrl()
        {
            using var server = TestServer.Start(FileBytes);
            server.ETag = "\"v1\"";
            WritePartial(server.Url, "XYZ", new PartialDownloadInfo
            {
                Url = "http://127.0.0.1:1/other",
                ETag = "\"v1\"",
                TotalBytes = FileBytes.Length,
            });

            await new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(server.Requests, Is.EqualTo(new[] { (default(string), default(string)) }));
        }

        [Test]
        public async Task DownloadFileAsync_DoesNotPromoteCompletePartialFromAnotherUrl()
        {
            using var server = TestServer.Start(FileBytes);
            WritePartial(server.Url, "XYZXYZXYZ", new PartialDownloadInfo
            {
                Url = "http://127.0.0.1:1/other",
                TotalBytes = FileBytes.Length,
            });

            await new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, null, null, CancellationToken.None);

            Assert.That(File.ReadAllBytes(_targetPath), Is.EqualTo(FileBytes));
            Assert.That(server.Requests, Has.Length.EqualTo(1));
        }

        [Test]
        public async Task DownloadFileAsync_FailsOnUnsolicitedPartialResponse()
        {
            using var server = TestServer.Start(FileBytes);
            server.PartialResponseBytes = 4;

            var exception = await CatchAsync<IOException>(() =>
                new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, null, null, CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("4 of 9"));
            Assert.That(File.Exists(_targetPath), Is.False);
        }

        [Test]
        public async Task DownloadFileAsync_AcceptsBareFileName()
        {
            using var server = TestServer.Start(FileBytes);
            var previousDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(_rootDirectory);
            try
            {
                await new ResumableDownloader().DownloadFileAsync(server.Url, "file.bin", FileBytes.Length, null, CancellationToken.None);
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }

            Assert.That(File.ReadAllBytes(Path.Combine(_rootDirectory, "file.bin")), Is.EqualTo(FileBytes));
        }

        [Test]
        public async Task DownloadFileAsync_FailsWithoutWriting_WhenSizeDiffersFromExpected()
        {
            using var server = TestServer.Start(FileBytes);

            var exception = await CatchAsync<IOException>(() =>
                new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, 20, null, CancellationToken.None));

            Assert.That(exception.Message, Does.Contain(server.Url));
            Assert.That(File.Exists(_targetPath), Is.False);
            Assert.That(File.Exists(PartialPath), Is.False);
        }

        [Test]
        public async Task DownloadFileAsync_NamesUrlAndKeepsPartial_OnHttpError()
        {
            using var server = TestServer.Start(FileBytes);
            server.StatusCode = 404;
            WritePartial(server.Url, "abc", new PartialDownloadInfo { ETag = "\"v1\"", TotalBytes = FileBytes.Length });

            var exception = await CatchAsync<WebRequestException>(() =>
                new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, CancellationToken.None));

            Assert.That(exception.ResponseCode, Is.EqualTo(404));
            Assert.That(exception.Message, Does.Contain(server.Url));
            Assert.That(File.ReadAllText(PartialPath), Is.EqualTo("abc"));
            Assert.That(File.Exists(_targetPath), Is.False);
        }

        [Test]
        public async Task DownloadFileAsync_KeepsPartialAndTarget_WhenCanceled()
        {
            using var server = TestServer.Start(FileBytes);
            server.HoldAfterBytes = 4;
            File.WriteAllText(_targetPath, "old");
            // The server sends 4 bytes and then holds the response open far longer than this.
            using var cancellation = new CancellationTokenSource(1000);

            await CatchAsync<OperationCanceledException>(() =>
                new ResumableDownloader().DownloadFileAsync(server.Url, _targetPath, FileBytes.Length, null, cancellation.Token));

            Assert.That(File.ReadAllText(_targetPath), Is.EqualTo("old"));
            Assert.That(new FileInfo(PartialPath).Length, Is.EqualTo(4));
        }

        private void WritePartial(string url, string content, PartialDownloadInfo info)
        {
            info.Url ??= url;
            File.WriteAllText(PartialPath, content);
            JsonStateStore.Save(InfoPath, info);
        }

        private static async Task<T> CatchAsync<T>(Func<Task> action)
            where T : Exception
        {
            try
            {
                await action();
            }
            catch (T exception)
            {
                return exception;
            }

            throw new AssertionException($"Expected {typeof(T).Name}.");
        }

        private sealed class SyncProgress : IProgress<DownloadProgress>
        {
            private readonly Action<DownloadProgress> _report;

            public SyncProgress(Action<DownloadProgress> report)
            {
                _report = report;
            }

            public void Report(DownloadProgress value)
            {
                _report(value);
            }
        }

        /// <summary>
        /// Serves one file with optional Range, If-Range, error, cut and hold behavior.
        /// </summary>
        private sealed class TestServer : IDisposable
        {
            private readonly byte[] _fileBytes;
            private readonly HttpListener _listener;
            private readonly CancellationTokenSource _stop = new();
            private readonly List<(string Range, string IfRange)> _requests = new();
            private readonly Task _serverTask;

            private TestServer(HttpListener listener, string url, byte[] fileBytes)
            {
                _listener = listener;
                Url = url;
                _fileBytes = fileBytes;
                _serverTask = Task.Run(RunAsync);
            }

            public string Url { get; }
            public bool RespectRange { get; set; } = true;
            public string ETag { get; set; }
            public int StatusCode { get; set; }
            public int? CutFirstResponseAfterBytes { get; set; }
            public int? HoldAfterBytes { get; set; }
            public int? PartialResponseBytes { get; set; }

            public IReadOnlyList<(string Range, string IfRange)> Requests
            {
                get
                {
                    lock (_requests)
                    {
                        return _requests.ToArray();
                    }
                }
            }

            public static TestServer Start(byte[] fileBytes)
            {
                var url = $"http://127.0.0.1:{GetFreePort()}/";
                var listener = new HttpListener();
                listener.Prefixes.Add(url);
                listener.Start();
                return new TestServer(listener, url, fileBytes);
            }

            public void Dispose()
            {
                _stop.Cancel();
                _listener.Close();
                try
                {
                    _serverTask.GetAwaiter().GetResult();
                }
                catch
                {
                    // Closing the listener interrupts pending accepts.
                }

                _stop.Dispose();
            }

            private async Task RunAsync()
            {
                while (!_stop.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch
                    {
                        return;
                    }

                    try
                    {
                        await RespondAsync(context);
                    }
                    catch
                    {
                        // The client may abort; the next request still gets served.
                    }
                }
            }

            private async Task RespondAsync(HttpListenerContext context)
            {
                var range = context.Request.Headers["Range"];
                var ifRange = context.Request.Headers["If-Range"];
                int requestIndex;
                lock (_requests)
                {
                    _requests.Add((range, ifRange));
                    requestIndex = _requests.Count - 1;
                }

                var response = context.Response;
                if (StatusCode != 0)
                {
                    response.StatusCode = StatusCode;
                    var body = Encoding.UTF8.GetBytes("error body");
                    await response.OutputStream.WriteAsync(body, 0, body.Length);
                    response.Close();
                    return;
                }

                if (ETag != null)
                {
                    response.AddHeader("ETag", ETag);
                }

                var start = 0;
                var usesRange = RespectRange &&
                                range != null &&
                                range.StartsWith("bytes=", StringComparison.Ordinal) &&
                                range.EndsWith("-", StringComparison.Ordinal) &&
                                int.TryParse(range.Substring(6, range.Length - 7), out start) &&
                                (ifRange == null || ifRange == ETag);
                if (usesRange && start >= _fileBytes.Length)
                {
                    response.StatusCode = 416;
                    response.AddHeader("Content-Range", $"bytes */{_fileBytes.Length}");
                    response.Close();
                    return;
                }

                if (!usesRange)
                {
                    start = 0;
                }

                var length = _fileBytes.Length - start;
                if (range == null && PartialResponseBytes.HasValue)
                {
                    // A misbehaving server: 206 for a request that asked for no range.
                    length = PartialResponseBytes.Value;
                    response.StatusCode = 206;
                    response.ContentLength64 = length;
                    response.AddHeader("Content-Range", $"bytes 0-{length - 1}/{_fileBytes.Length}");
                    await response.OutputStream.WriteAsync(_fileBytes, 0, length);
                    response.Close();
                    return;
                }

                response.StatusCode = usesRange ? 206 : 200;
                response.ContentLength64 = length;
                if (usesRange)
                {
                    response.AddHeader("Content-Range", $"bytes {start}-{_fileBytes.Length - 1}/{_fileBytes.Length}");
                }

                var sendBytes = HoldAfterBytes ?? (requestIndex == 0 ? CutFirstResponseAfterBytes : null);
                if (sendBytes.HasValue)
                {
                    await response.OutputStream.WriteAsync(_fileBytes, start, sendBytes.Value);
                    await response.OutputStream.FlushAsync();
                    try
                    {
                        await Task.Delay(HoldAfterBytes.HasValue ? 10000 : 200, _stop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    response.Abort();
                    return;
                }

                await response.OutputStream.WriteAsync(_fileBytes, start, length);
                response.Close();
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
