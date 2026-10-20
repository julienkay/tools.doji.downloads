using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.Downloads.Editor.Tests
{
    /// <summary>
    /// Covers generic sequential queue executor independent from HF Hub-specific model sync work.
    /// </summary>
    internal sealed class DownloadEditorPersistentItemQueueExecutorTests
    {
        [Test]
        public async Task ExecuteAsync_ProcessesItemsInOrderAndClearsStateOnSuccess()
        {
            var state = PersistentItemQueueState.Create(new[] { "a", "b" });
            var processed = new List<string>();
            var savedSnapshots = new List<(string CurrentItemKey, string PendingKeys)>();
            var context = CreateContext();

            await EditorPersistentItemQueueExecutor.ExecuteAsync(
                state,
                new[] { "a", "b" },
                item => item,
                queueState => savedSnapshots.Add((queueState.CurrentItemKey, string.Join(",", queueState.PendingItemKeys))),
                (item, index, totalCount, operationContext) =>
                {
                    processed.Add($"{index}/{totalCount}:{item}");
                    return Task.CompletedTask;
                },
                context);

            Assert.That(processed, Is.EqualTo(new[] { "0/2:a", "1/2:b" }));
            Assert.That(state.CurrentItemKey, Is.Null);
            Assert.That(state.PendingItemKeys, Is.Empty);
            Assert.That(savedSnapshots, Is.EqualTo(new[]
            {
                ("a", "a,b"),
                (null, "b"),
                ("b", "b"),
                (null, string.Empty)
            }));
        }

        [Test]
        public void ExecuteAsync_LeavesCurrentItemAndPendingQueueOnFailure()
        {
            var state = PersistentItemQueueState.Create(new[] { "a", "b" });
            var savedSnapshots = new List<(string CurrentItemKey, string PendingKeys)>();
            var context = CreateContext();

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await EditorPersistentItemQueueExecutor.ExecuteAsync(
                    state,
                    new[] { "a", "b" },
                    item => item,
                    queueState => savedSnapshots.Add((queueState.CurrentItemKey, string.Join(",", queueState.PendingItemKeys))),
                    (item, index, totalCount, operationContext) => item == "a"
                        ? throw new InvalidOperationException("boom")
                        : Task.CompletedTask,
                    context);
            });

            Assert.That(state.CurrentItemKey, Is.EqualTo("a"));
            Assert.That(state.PendingItemKeys, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(savedSnapshots, Is.EqualTo(new[]
            {
                ("a", "a,b")
            }));
        }

        [Test]
        public async Task ExecuteAsync_SkipsItemsThatAreNoLongerPending()
        {
            var state = PersistentItemQueueState.Create(new[] { "b", "c" });
            var processed = new List<string>();

            await EditorPersistentItemQueueExecutor.ExecuteAsync(
                state,
                new[] { "a", "b", "c" },
                item => item,
                _ => { },
                (item, index, totalCount, operationContext) =>
                {
                    processed.Add($"{index}/{totalCount}:{item}");
                    return Task.CompletedTask;
                },
                CreateContext());

            Assert.That(processed, Is.EqualTo(new[] { "0/2:b", "1/2:c" }));
            Assert.That(state.PendingItemKeys, Is.Empty);
        }

        [Test]
        public void ExecuteAsync_StopsBeforeNextItem_WhenCanceled()
        {
            var state = PersistentItemQueueState.Create(new[] { "a", "b" });
            var processed = new List<string>();
            using var cancellation = new CancellationTokenSource();
            var context = CreateContext(cancellation.Token);

            Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await EditorPersistentItemQueueExecutor.ExecuteAsync(
                    state,
                    new[] { "a", "b" },
                    item => item,
                    _ => { },
                    (item, index, totalCount, operationContext) =>
                    {
                        processed.Add(item);
                        cancellation.Cancel();
                        return Task.CompletedTask;
                    },
                    context);
            });

            Assert.That(processed, Is.EqualTo(new[] { "a" }));
            Assert.That(state.PendingItemKeys, Is.EqualTo(new[] { "b" }));
        }

        private static EditorPersistentOperationContext CreateContext(CancellationToken cancellationToken = default)
        {
            return new EditorPersistentOperationContext(
                cancellationToken,
                (_, _) => { },
                action => action?.Invoke());
        }
    }
}
