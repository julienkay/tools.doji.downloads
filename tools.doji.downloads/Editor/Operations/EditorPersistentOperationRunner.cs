using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Doji.Downloads.Editor
{
    /// <summary>
    /// Runs editor operations with persisted state, Unity Progress UI, and domain-reload-safe restart.
    /// </summary>
    public sealed class EditorPersistentOperationRunner
    {
        private readonly string _operationName;
        private readonly string _initialDescription;
        private readonly string _statePath;
        private int _progressId = -1;
        private CancellationTokenSource _cancellationTokenSource;

        public EditorPersistentOperationRunner(string operationName, string initialDescription, string statePath)
        {
            _operationName = operationName;
            _initialDescription = initialDescription;
            _statePath = statePath;
        }

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Returns true when persisted state exists and operation should be resumed after reload.
        /// </summary>
        public bool HasPersistedState()
        {
            return File.Exists(_statePath);
        }

        /// <summary>
        /// Starts a new operation or resumes persisted one using the same execution callback.
        /// Returning true from `runAsync` deletes persisted state; false leaves it for retry/resume.
        /// </summary>
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
        /// Persists latest state snapshot so a later reload can continue from the remaining item keys.
        /// </summary>
        public void SaveState(PersistentItemQueueState state)
        {
            JsonStateStore.Save(_statePath, state);
        }

        /// <summary>
        /// Deletes persisted state after operation completes successfully.
        /// </summary>
        public void DeleteState()
        {
            JsonStateStore.Delete(_statePath);
        }

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
                QueueOnMainThread(() =>
                {
                    RemoveProgressIfExists();
                    onCanceled?.Invoke();
                });
            }
            catch (Exception exception)
            {
                QueueOnMainThread(() =>
                {
                    onUnhandledException?.Invoke(exception);
                    RemoveProgressIfExists();
                });
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                IsRunning = false;
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
            EnsureProgress();
            Progress.Report(_progressId, Mathf.Clamp01(progress), description);
        }

        public void RemoveProgressIfExists()
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
