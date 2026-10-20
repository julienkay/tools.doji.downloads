using System;
using System.Threading;

namespace Doji.Downloads.Editor
{
    /// <summary>
    /// What a running <see cref="EditorPersistentOperationRunner"/> operation gets: cancellation, progress and main-thread dispatch.
    /// </summary>
    public sealed class EditorPersistentOperationContext
    {
        private readonly Action<float, string> _reportProgress;
        private readonly Action<Action> _queueOnMainThread;

        internal EditorPersistentOperationContext(
            CancellationToken cancellationToken,
            Action<float, string> reportProgress,
            Action<Action> queueOnMainThread)
        {
            CancellationToken = cancellationToken;
            _reportProgress = reportProgress;
            _queueOnMainThread = queueOnMainThread;
        }

        /// <summary>
        /// Cancelled by <see cref="EditorPersistentOperationRunner.Cancel"/> or the Background Tasks cancel button.
        /// Let the resulting <see cref="System.OperationCanceledException"/> through, so the runner ends the operation.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Updates the operation's Background Tasks entry.
        /// </summary>
        public void ReportProgress(float progress, string description)
        {
            _reportProgress?.Invoke(progress, description);
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the main thread on a later Editor update.
        /// </summary>
        public void QueueOnMainThread(Action action)
        {
            _queueOnMainThread?.Invoke(action);
        }
    }
}
