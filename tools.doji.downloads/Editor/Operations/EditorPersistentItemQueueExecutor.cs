using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doji.Downloads.Editor
{
    /// <summary>
    /// Executes sequential item queues over persisted stable keys while keeping save/resume semantics outside item logic.
    /// </summary>
    public static class EditorPersistentItemQueueExecutor
    {
        /// <summary>
        /// Processes queued items in order, persisting current item and remaining keys before and after each successful step.
        /// </summary>
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

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var itemKey = getItemKey(item);
                state.CurrentItemKey = itemKey;
                saveState(state);

                await processItemAsync(item, index, items.Count, context);

                state.PendingItemKeys.Remove(itemKey);
                state.CurrentItemKey = null;
                saveState(state);
            }
        }
    }
}
