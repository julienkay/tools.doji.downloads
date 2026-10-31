using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Doji.Downloads.Editor.Tests
{
    /// <summary>
    /// Covers main-thread dispatch through <see cref="EditorMainThread"/>.
    /// </summary>
    internal sealed class DownloadEditorMainThreadTests
    {
        private const int MaxFrames = 600;

        [UnityTest]
        public IEnumerator Post_FromWorkerThread_RunsOnMainThread()
        {
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var ranOnThreadId = -1;

            var post = Task.Run(() => EditorMainThread.Post(() => ranOnThreadId = Thread.CurrentThread.ManagedThreadId));
            for (var frame = 0; frame < MaxFrames && (!post.IsCompleted || ranOnThreadId == -1); frame++)
            {
                yield return null;
            }

            Assert.That(ranOnThreadId, Is.EqualTo(mainThreadId));
        }

        [UnityTest]
        public IEnumerator Post_KeepsOrder()
        {
            var order = new List<int>();
            for (var i = 0; i < 5; i++)
            {
                var value = i;
                EditorMainThread.Post(() => order.Add(value));
            }

            for (var frame = 0; frame < MaxFrames && order.Count < 5; frame++)
            {
                yield return null;
            }

            Assert.That(order, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
        }

        [UnityTest]
        public IEnumerator Post_ExceptionDoesNotStopTheQueue()
        {
            var ran = false;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: dispatch test"));

            EditorMainThread.Post(() => throw new InvalidOperationException("dispatch test"));
            EditorMainThread.Post(() => ran = true);

            for (var frame = 0; frame < MaxFrames && !ran; frame++)
            {
                yield return null;
            }

            Assert.That(ran, Is.True);
        }

        [UnityTest]
        public IEnumerator Post_RunsEachActionOnce()
        {
            var runs = 0;
            EditorMainThread.Post(() => runs++);

            for (var frame = 0; frame < 10; frame++)
            {
                yield return null;
            }

            Assert.That(runs, Is.EqualTo(1));
        }
    }
}
