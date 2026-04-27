using System;
using System.Threading;

namespace Doji.Downloads.Editor
{
    /// <summary>
    /// Exposes cancellation, progress, and main-thread callback hooks to editor-persistent operations.
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

        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Updates the shared editor progress entry for the active operation.
        /// </summary>
        public void ReportProgress(float progress, string description)
        {
            _reportProgress?.Invoke(progress, description);
        }

        /// <summary>
        /// Schedules work back onto the main thread after background continuation completes.
        /// </summary>
        public void QueueOnMainThread(Action action)
        {
            _queueOnMainThread?.Invoke(action);
        }
    }
}
