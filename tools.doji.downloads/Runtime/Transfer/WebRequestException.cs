using System;

namespace Doji.Downloads
{
    /// <summary>
    /// A web request that did not succeed, with the URL and the cause.
    /// </summary>
    public sealed class WebRequestException : Exception
    {
        /// <param name="url">The requested URL.</param>
        /// <param name="responseCode">The HTTP status code, or 0 when no response arrived.</param>
        /// <param name="error">Unity's error text for the request.</param>
        public WebRequestException(string url, long responseCode, string error)
            : base(BuildMessage(url, responseCode, error))
        {
            Url = url;
            ResponseCode = responseCode;
            Error = error;
        }

        /// <summary>
        /// The requested URL.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// The HTTP status code, or 0 when the request failed before a response arrived.
        /// </summary>
        public long ResponseCode { get; }

        /// <summary>
        /// Unity's error text for the request.
        /// </summary>
        public string Error { get; }

        private static string BuildMessage(string url, long responseCode, string error)
        {
            return responseCode > 0
                ? $"Request to '{url}' failed ({responseCode}): {error}"
                : $"Request to '{url}' failed: {error}";
        }
    }
}
