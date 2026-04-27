using System;
using System.IO;
using NUnit.Framework;

namespace Doji.Downloads.Editor.Tests
{
    /// <summary>
    /// Covers generic downloader durable-state helpers independent from HF Hub-specific sync behavior.
    /// </summary>
    internal sealed class DownloadPersistentStateStoreTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.Downloads.StateStoreTests",
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
        public void Create_RemovesDuplicateAndBlankKeys()
        {
            var state = PersistentItemQueueState.Create(new[] { "pkg:model-a", "", "pkg:model-a", null, "pkg:model-b" });

            Assert.That(state.PendingItemKeys, Is.EqualTo(new[] { "pkg:model-a", "pkg:model-b" }));
            Assert.That(state.CurrentItemKey, Is.Null);
        }

        [Test]
        public void SaveLoadAndDelete_RoundTripsState()
        {
            var path = Path.Combine(_rootDirectory, "sync-state.json");
            var state = new PersistentItemQueueState
            {
                CurrentItemKey = "pkg:model-a",
                PendingItemKeys = { "pkg:model-a", "pkg:model-b" }
            };

            JsonStateStore.Save(path, state);
            var loaded = JsonStateStore.Load<PersistentItemQueueState>(path);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.CurrentItemKey, Is.EqualTo("pkg:model-a"));
            Assert.That(loaded.PendingItemKeys, Is.EqualTo(new[] { "pkg:model-a", "pkg:model-b" }));

            JsonStateStore.Delete(path);

            Assert.That(File.Exists(path), Is.False);
            Assert.That(JsonStateStore.Load<PersistentItemQueueState>(path), Is.Null);
        }

        [Test]
        public void Load_ReturnsNullForInvalidJson()
        {
            var path = Path.Combine(_rootDirectory, "broken.json");
            File.WriteAllText(path, "{ invalid json");

            Assert.That(JsonStateStore.Load<PersistentItemQueueState>(path), Is.Null);
        }
    }
}
