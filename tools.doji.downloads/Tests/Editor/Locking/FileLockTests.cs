using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.Downloads.Editor.Tests
{
    /// <summary>
    /// Covers waiting for, timing out on and cancelling a held <see cref="FileLock"/>.
    /// </summary>
    internal sealed class FileLockTests
    {
        private string _rootDirectory;
        private string _lockPath;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.Downloads.FileLockTests", Guid.NewGuid().ToString("N"));
            _lockPath = Path.Combine(_rootDirectory, "file.lock");
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
        public void TryAcquire_FailsWhileLockIsHeld()
        {
            Assert.That(FileLock.TryAcquire(_lockPath, out var held), Is.True);
            using (held)
            {
                Assert.That(FileLock.TryAcquire(_lockPath, out var second), Is.False);
                Assert.That(second, Is.Null);
            }

            Assert.That(FileLock.TryAcquire(_lockPath, out var afterRelease), Is.True);
            afterRelease.Dispose();
        }

        [Test]
        public async Task AcquireAsync_WaitsUntilLockIsReleased()
        {
            var held = await FileLock.AcquireAsync(_lockPath, TimeSpan.Zero);
            var waiting = FileLock.AcquireAsync(_lockPath, TimeSpan.FromSeconds(5));

            await Task.Delay(300);
            Assert.That(waiting.IsCompleted, Is.False);

            held.Dispose();
            using var acquired = await waiting;
            Assert.That(acquired, Is.Not.Null);
        }

        [Test]
        public async Task AcquireAsync_ThrowsTimeoutException_WhenLockStaysHeld()
        {
            using var held = await FileLock.AcquireAsync(_lockPath, TimeSpan.Zero);

            try
            {
                using var _ = await FileLock.AcquireAsync(_lockPath, TimeSpan.FromMilliseconds(250));
                Assert.Fail("Expected TimeoutException.");
            }
            catch (TimeoutException exception)
            {
                Assert.That(exception.Message, Does.Contain(_lockPath));
            }
        }

        [Test]
        public async Task AcquireAsync_ThrowsWhenCanceled()
        {
            using var held = await FileLock.AcquireAsync(_lockPath, TimeSpan.Zero);
            using var cancellation = new CancellationTokenSource(250);

            try
            {
                using var _ = await FileLock.AcquireAsync(_lockPath, TimeSpan.FromSeconds(10), cancellation.Token);
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
