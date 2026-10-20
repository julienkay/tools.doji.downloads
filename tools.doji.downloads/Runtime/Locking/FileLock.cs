using System;
using System.IO;
using System.Threading;

namespace Doji.Downloads
{
    /// <summary>
    /// Provides coarse file-level locking for durable download work.
    /// </summary>
    public sealed class FileLock : IDisposable
    {
        private readonly FileStream _lockStream;

        private FileLock(FileStream lockStream)
        {
            _lockStream = lockStream;
        }

        /// <summary>
        /// Acquires an exclusive lock for the target path, waiting until timeout expires.
        /// </summary>
        public static FileLock Acquire(string lockPath, TimeSpan timeout)
        {
            var lockDirectory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(lockDirectory))
            {
                Directory.CreateDirectory(lockDirectory);
            }

            var startTime = DateTime.UtcNow;
            while (true)
            {
                try
                {
                    var stream = new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                    return new FileLock(stream);
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow - startTime >= timeout)
                    {
                        throw;
                    }

                    Thread.Sleep(100);
                }
            }
        }

        public void Dispose()
        {
            _lockStream.Dispose();
        }
    }
}
