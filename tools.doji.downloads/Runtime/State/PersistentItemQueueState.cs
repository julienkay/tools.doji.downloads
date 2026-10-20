using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.Downloads
{
    /// <summary>
    /// Stores minimal durable state for sequential work queued by stable item keys.
    /// </summary>
    [Serializable]
    public sealed class PersistentItemQueueState
    {
        /// <summary>
        /// The key of the item in progress, or null between items.
        /// </summary>
        public string CurrentItemKey { get; set; }

        /// <summary>
        /// The keys of the items that are not done yet, in order. Includes the current item.
        /// </summary>
        public List<string> PendingItemKeys { get; set; } = new();

        /// <summary>
        /// Builds persisted queue state from stable item keys, preserving first-seen order and removing duplicates.
        /// </summary>
        public static PersistentItemQueueState Create(IEnumerable<string> itemKeys)
        {
            return new PersistentItemQueueState
            {
                PendingItemKeys = itemKeys?
                    .Where(itemKey => !string.IsNullOrWhiteSpace(itemKey))
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
                    ?? new List<string>()
            };
        }
    }
}
