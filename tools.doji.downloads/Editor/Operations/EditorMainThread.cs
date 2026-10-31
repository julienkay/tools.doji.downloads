using System;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine;

namespace Doji.Downloads.Editor
{
    /// <summary>
    /// Runs posted actions on the Editor main thread, in post order, on the next <see cref="EditorApplication.update"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="EditorApplication.update"/> keeps ticking while the Editor is unfocused and driven by a tool such as
    /// the Unity CLI; <see cref="EditorApplication.delayCall"/> does not. A domain reload drops actions that have not run.
    /// </remarks>
    [InitializeOnLoad]
    internal static class EditorMainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();

        static EditorMainThread()
        {
            EditorApplication.update -= Drain;
            EditorApplication.update += Drain;
        }

        /// <summary>
        /// Queues <paramref name="action"/> to run once on the main thread. Safe to call from any thread.
        /// </summary>
        public static void Post(Action action)
        {
            if (action != null)
            {
                Queue.Enqueue(action);
            }
        }

        private static void Drain()
        {
            // Run only what was queued before this update, so an action that posts again cannot stall the Editor.
            var count = Queue.Count;
            for (var i = 0; i < count && Queue.TryDequeue(out var action); i++)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
