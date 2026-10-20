using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Doji.Downloads
{
    /// <summary>
    /// Awaitable <see cref="UnityWebRequest"/> sending for Editor and player code.
    /// </summary>
    public static class UnityWebRequestExtensions
    {
        /// <summary>
        /// Sends the request and completes when Unity reports it done, without polling.
        /// </summary>
        /// <remarks>
        /// Call from the Unity main thread. Cancellation aborts the request on the thread that sent it.
        /// </remarks>
        /// <exception cref="OperationCanceledException">The token was cancelled before the request completed.</exception>
        /// <exception cref="WebRequestException">The request failed with a connection, protocol or data error.</exception>
        public static Task SendAsync(this UnityWebRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Continuations run asynchronously so callers never dispose the request inside Unity's completed callback.
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var synchronizationContext = SynchronizationContext.Current;
            var operation = request.SendWebRequest();
            var registration = cancellationToken.Register(() =>
            {
                if (synchronizationContext != null)
                {
                    synchronizationContext.Post(_ => AbortIfRunning(request), null);
                }
                else
                {
                    AbortIfRunning(request);
                }
            });

            operation.completed += _ =>
            {
                registration.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                else if (request.result != UnityWebRequest.Result.Success)
                {
                    completion.TrySetException(new WebRequestException(request.url, request.responseCode, request.error));
                }
                else
                {
                    completion.TrySetResult(true);
                }
            };

            return completion.Task;
        }

        private static void AbortIfRunning(UnityWebRequest request)
        {
            try
            {
                if (!request.isDone)
                {
                    request.Abort();
                }
            }
            catch (Exception)
            {
                // The caller may already have disposed a request that completed during cancellation.
            }
        }
    }
}
