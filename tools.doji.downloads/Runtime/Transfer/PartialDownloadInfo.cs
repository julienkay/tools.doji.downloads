using System;

namespace Doji.Downloads
{
    /// <summary>
    /// Where a partial download came from and what the server said about the file when it started.
    /// </summary>
    /// <remarks>
    /// Saved next to the partial file. A resume needs the same URL and sends the validator as <c>If-Range</c>, so a
    /// changed remote file restarts from zero instead of appending bytes of another version. A partial file without
    /// this info, from another URL, or incomplete without a validator is discarded.
    /// </remarks>
    [Serializable]
    internal sealed class PartialDownloadInfo
    {
        public string Url { get; set; }
        public string ETag { get; set; }
        public string LastModified { get; set; }
        public long? TotalBytes { get; set; }

        /// <summary>
        /// The <c>If-Range</c> value: a strong ETag, else <c>Last-Modified</c>. Weak ETags are not allowed in <c>If-Range</c>.
        /// </summary>
        public string RangeValidator
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ETag) && !ETag.StartsWith("W/", StringComparison.Ordinal))
                {
                    return ETag;
                }

                return string.IsNullOrWhiteSpace(LastModified) ? null : LastModified;
            }
        }
    }
}
