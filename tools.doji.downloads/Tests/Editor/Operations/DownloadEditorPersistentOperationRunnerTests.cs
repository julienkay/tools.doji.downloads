using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.Downloads.Editor.Tests
{
    /// <summary>
    /// Covers generic editor-side recovery runner behavior independent from HF Hub-specific sync logic.
    /// </summary>
    internal sealed class DownloadEditorPersistentOperationRunnerTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.Downloads.EditorRunnerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        [Test]
        public async Task StartOrResume_UsesPersistedStateAndDeletesItOnSuccess()
        {
            var statePath = Path.Combine(_rootDirectory, "sync-state.json");
            var runner = new EditorPersistentOperationRunner("Test Sync", "Running", statePath);
            var persistedState = new PersistentItemQueueState
            {
                CurrentItemKey = "pkg:model-a",
                PendingItemKeys = { "pkg:model-a" }
            };
            JsonStateStore.Save(statePath, persistedState);

            var completion = new TaskCompletionSource<PersistentItemQueueState>();

            runner.StartOrResume(
                () => throw new AssertionException("Runner should prefer persisted state over factory state."),
                (state, context) =>
                {
                    completion.TrySetResult(state);
                    return Task.FromResult(true);
                },
                exception => completion.TrySetException(exception));

            var loadedState = await completion.Task;

            Assert.That(loadedState.CurrentItemKey, Is.EqualTo("pkg:model-a"));
            Assert.That(loadedState.PendingItemKeys, Is.EqualTo(new[] { "pkg:model-a" }));
            await WaitUntilAsync(() => !File.Exists(statePath));
            Assert.That(runner.HasPersistedState(), Is.False);
        }

        [Test]
        public async Task StartOrResume_KeepsStateFileWhenOperationRequestsResume()
        {
            var statePath = Path.Combine(_rootDirectory, "sync-state.json");
            var runner = new EditorPersistentOperationRunner("Test Sync", "Running", statePath);
            var completion = new TaskCompletionSource<bool>();

            runner.StartOrResume(
                () => PersistentItemQueueState.Create(new[] { "pkg:model-a" }),
                (state, context) =>
                {
                    completion.TrySetResult(true);
                    return Task.FromResult(false);
                },
                exception => completion.TrySetException(exception));

            await completion.Task;
            await WaitUntilAsync(() => File.Exists(statePath));
            Assert.That(runner.HasPersistedState(), Is.True);
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            for (var index = 0; index < 50; index++)
            {
                if (predicate())
                {
                    return;
                }

                await Task.Delay(20);
            }

            throw new AssertionException("Timed out waiting for async runner state.");
        }
    }
}
