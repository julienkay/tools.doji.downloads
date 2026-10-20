using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doji.Downloads.Editor
{
    /// <summary>
    /// Processes a queue of items one by one and saves progress between items, so item logic never handles save and resume.
    /// </summary>
    public static class EditorPersistentItemQueueExecutor
    {
        /// <summary>
        /// Processes the items whose keys are still pending in <paramref name="state"/>, in the order of <paramref name="items"/>.
        /// </summary>
        /// <remarks>
        /// Pass every item: items whose keys are not pending are skipped, so a resumed queue continues where it stopped.
        /// The state is saved before each item (with <see cref="PersistentItemQueueState.CurrentItemKey"/> set) and after it
        /// (with the key removed). Cancellation is checked before each item.
        /// </remarks>
        /// <param name="getItemKey">Returns the stable key that identifies an item in the saved state.</param>
        /// <param name="saveState">Saves the state, usually <see cref="EditorPersistentOperationRunner.SaveState"/>.</param>
        /// <param name="processItemAsync">Processes one item; receives the item, its index and the number of items to process.</param>
        public static async Task ExecuteAsync<TItem>(
            PersistentItemQueueState state,
            IReadOnlyList<TItem> items,
            Func<TItem, string> getItemKey,
            Action<PersistentItemQueueState> saveState,
            Func<TItem, int, int, EditorPersistentOperationContext, Task> processItemAsync,
            EditorPersistentOperationContext context)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (getItemKey == null)
            {
                throw new ArgumentNullException(nameof(getItemKey));
            }

            if (saveState == null)
            {
                throw new ArgumentNullException(nameof(saveState));
            }

            if (processItemAsync == null)
            {
                throw new ArgumentNullException(nameof(processItemAsync));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var pendingKeys = new HashSet<string>(state.PendingItemKeys, StringComparer.Ordinal);
            var queue = new List<(TItem Item, string Key)>();
            foreach (var item in items)
            {
                var key = getItemKey(item);
                if (pendingKeys.Remove(key))
                {
                    queue.Add((item, key));
                }
            }

            for (var index = 0; index < queue.Count; index++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var (item, itemKey) = queue[index];
                state.CurrentItemKey = itemKey;
                saveState(state);

                await processItemAsync(item, index, queue.Count, context);

                state.PendingItemKeys.Remove(itemKey);
                state.CurrentItemKey = null;
                saveState(state);
            }
        }
    }
}
