using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Doji.Downloads.Editor
{
    /// <summary>
    /// Runs a long Editor operation that survives domain reloads and Editor restarts, with a Background Tasks entry.
    /// </summary>
    /// <remarks>
    /// The runner saves a <see cref="PersistentItemQueueState"/> before the operation starts and deletes it when the
    /// operation completes or is cancelled. Call <see cref="StartOrResume"/> again after a reload (for example from an
    /// <c>[InitializeOnLoad]</c> class when <see cref="HasPersistedState"/> is true) to continue. The runner owns the
    /// Background Tasks entry: it creates it, reports to it, and removes it when the operation ends.
    /// </remarks>
    public sealed class EditorPersistentOperationRunner
    {
        private readonly string _operationName;
        private readonly string _initialDescription;
        private readonly string _statePath;
        private int _progressId = -1;

        /// <summary>
        /// The Background Tasks entry id, or -1 when there is none.
        /// </summary>
        internal int ProgressId => _progressId;
        private CancellationTokenSource _cancellationTokenSource;

        /// <param name="operationName">The Background Tasks title.</param>
        /// <param name="initialDescription">The Background Tasks description before the first progress report.</param>
        /// <param name="statePath">The JSON file that holds the state between reloads.</param>
        public EditorPersistentOperationRunner(string operationName, string initialDescription, string statePath)
        {
            _operationName = operationName;
            _initialDescription = initialDescription;
            _statePath = statePath;
        }

        /// <summary>
        /// True while the operation runs in this domain. A domain reload stops it; the saved state remains.
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// True when a saved state exists, so an interrupted operation can resume.
        /// </summary>
        public bool HasPersistedState()
        {
            return File.Exists(_statePath);
        }

        /// <summary>
        /// Starts a new operation, or resumes the saved one, with the same callback. Does nothing while running.
        /// </summary>
        /// <param name="createState">Creates the state when none is saved.</param>
        /// <param name="runAsync">
        /// The operation. Return true when it is done, so the state is deleted; false keeps the state for a later resume.
        /// </param>
        /// <param name="onUnhandledException">Runs on the main thread when the operation throws. The state stays.</param>
        /// <param name="onCanceled">
        /// Runs on the main thread after <see cref="Cancel"/> or the Background Tasks cancel button stopped the
        /// operation. The persisted state is already deleted, so a cancelled operation does not resume.
        /// </param>
        public void StartOrResume(
            Func<PersistentItemQueueState> createState,
            Func<PersistentItemQueueState, EditorPersistentOperationContext, Task<bool>> runAsync,
            Action<Exception> onUnhandledException,
            Action onCanceled = null)
        {
            if (IsRunning)
            {
                return;
            }

            var state = LoadState();
            if (state == null)
            {
                state = createState?.Invoke() ?? throw new InvalidOperationException("Missing operation state factory.");
                SaveState(state);
            }

            _ = RunAsync(state, runAsync, onUnhandledException, onCanceled);
        }

        /// <summary>
        /// Requests cancellation of the running operation through its <see cref="EditorPersistentOperationContext.CancellationToken"/>.
        /// </summary>
        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Saves the state so a later reload continues from the remaining item keys.
        /// </summary>
        public void SaveState(PersistentItemQueueState state)
        {
            JsonStateStore.Save(_statePath, state);
        }

        /// <summary>
        /// Deletes the saved state, so the operation does not resume.
        /// </summary>
        public void DeleteState()
        {
            JsonStateStore.Delete(_statePath);
        }

        /// <summary>
        /// Reads the saved state, or null when there is none or it is invalid.
        /// </summary>
        public PersistentItemQueueState LoadState()
        {
            return JsonStateStore.Load<PersistentItemQueueState>(_statePath);
        }

        private async Task RunAsync(
            PersistentItemQueueState state,
            Func<PersistentItemQueueState, EditorPersistentOperationContext, Task<bool>> runAsync,
            Action<Exception> onUnhandledException,
            Action onCanceled)
        {
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            try
            {
                EnsureProgress();
                var context = new EditorPersistentOperationContext(
                    cancellationToken,
                    ReportProgress,
                    QueueOnMainThread);
                var completed = await runAsync(state, context);
                if (completed)
                {
                    DeleteState();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DeleteState();
                if (onCanceled != null)
                {
                    QueueOnMainThread(onCanceled);
                }
            }
            catch (Exception exception)
            {
                QueueOnMainThread(() => onUnhandledException?.Invoke(exception));
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                IsRunning = false;
                RemoveProgressIfExists();
            }
        }

        private void EnsureProgress()
        {
            if (!Progress.Exists(_progressId))
            {
                _progressId = Progress.Start(_operationName, _initialDescription);
                Progress.RegisterCancelCallback(_progressId, () =>
                {
                    Cancel();
                    return true;
                });
            }
        }

        private void ReportProgress(float progress, string description)
        {
            // Progress callbacks posted before the operation ended must not create a new, orphaned entry.
            if (!IsRunning)
            {
                return;
            }

            EnsureProgress();
            Progress.Report(_progressId, Mathf.Clamp01(progress), description);
        }

        private void RemoveProgressIfExists()
        {
            if (Progress.Exists(_progressId))
            {
                Progress.Remove(_progressId);
            }

            _progressId = -1;
        }

        private static void QueueOnMainThread(Action action)
        {
            EditorApplication.delayCall += () => action?.Invoke();
        }
    }
}
