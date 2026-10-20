namespace Doji.Downloads
{
    /// <summary>
    /// Describes byte-level progress for one download operation.
    /// </summary>
    public readonly struct DownloadProgress
    {
        /// <summary>
        /// Captures bytes already persisted and the best-known total size for the active transfer.
        /// </summary>
        public DownloadProgress(long downloadedBytes, long? totalBytes)
        {
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
        }

        public long DownloadedBytes { get; }
        public long? TotalBytes { get; }
    }
}
