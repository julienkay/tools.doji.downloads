using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Doji.Downloads
{
    /// <summary>
    /// An exclusive lock on a lock file, so concurrent work in this or another process does not write the same files.
    /// </summary>
    /// <remarks>
    /// The lock is held while the lock file is open. Dispose releases it; the lock file itself stays.
    /// </remarks>
    public sealed class FileLock : IDisposable
    {
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);
        private readonly FileStream _lockStream;

        private FileLock(FileStream lockStream)
        {
            _lockStream = lockStream;
        }

        /// <summary>
        /// Takes the lock if it is free, without waiting.
        /// </summary>
        public static bool TryAcquire(string lockPath, out FileLock fileLock)
        {
            if (string.IsNullOrWhiteSpace(lockPath))
            {
                throw new ArgumentException("Lock path is required.", nameof(lockPath));
            }

            var lockDirectory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(lockDirectory))
            {
                Directory.CreateDirectory(lockDirectory);
            }

            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                fileLock = new FileLock(stream);
                return true;
            }
            catch (IOException)
            {
                fileLock = null;
                return false;
            }
        }

        /// <summary>
        /// Waits for the lock without blocking the calling thread.
        /// </summary>
        /// <exception cref="TimeoutException">The lock was not free within <paramref name="timeout"/>.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled while waiting.</exception>
        public static async Task<FileLock> AcquireAsync(
            string lockPath,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryAcquire(lockPath, out var fileLock))
                {
                    return fileLock;
                }

                if (DateTime.UtcNow - startTime >= timeout)
                {
                    throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0.#} s waiting for lock '{lockPath}'.");
                }

                await Task.Delay(RetryInterval, cancellationToken);
            }
        }

        /// <summary>
        /// Releases the lock.
        /// </summary>
        public void Dispose()
        {
            _lockStream.Dispose();
        }
    }
}
