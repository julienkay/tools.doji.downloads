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

        /// <summary>
        /// Bytes on disk, including bytes from an earlier attempt that this download resumed.
        /// </summary>
        public long DownloadedBytes { get; }

        /// <summary>
        /// The full size of the file, or null when the server did not say.
        /// </summary>
        public long? TotalBytes { get; }
    }
}
