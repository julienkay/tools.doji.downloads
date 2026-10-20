# Persistent Editor operations

A domain reload stops every running `async` method in the Editor. That happens
whenever scripts recompile or you enter Play mode. For a download that takes
minutes, that is not good enough.

`EditorPersistentOperationRunner` and `EditorPersistentItemQueueExecutor` keep
a queue of work on disk, so the work continues after a domain reload or an
Editor restart:

- the **runner** saves the queue, shows an entry in the **Background Tasks**
  window with a cancel button, and deletes the queue when the work is done or
  cancelled
- the **executor** processes the queue one item at a time and saves it between
  items

Your item code only does the work. It never saves or resumes anything itself.

## Example: download a list of files

```csharp
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Doji.Downloads;
using Doji.Downloads.Editor;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class ModelDownloads
{
    static readonly string[] Urls =
    {
        "https://example.com/models/encoder.onnx",
        "https://example.com/models/decoder.onnx",
    };

    static readonly EditorPersistentOperationRunner Runner = new(
        "Model Downloads",
        "Preparing",
        "Library/ModelDownloads/state.json");

    // Continue an interrupted queue after a domain reload or an Editor restart.
    static ModelDownloads()
    {
        EditorApplication.delayCall += () =>
        {
            if (Runner.HasPersistedState())
            {
                Start();
            }
        };
    }

    [MenuItem("Tools/Download Models")]
    static void Start()
    {
        Runner.StartOrResume(
            () => PersistentItemQueueState.Create(Urls),
            RunAsync,
            exception => Debug.LogError(exception.Message),
            () => Debug.Log("Model download canceled."));
    }

    static async Task<bool> RunAsync(PersistentItemQueueState state, EditorPersistentOperationContext context)
    {
        var downloader = new ResumableDownloader();
        await EditorPersistentItemQueueExecutor.ExecuteAsync(
            state,
            Urls,
            url => url,
            Runner.SaveState,
            async (url, index, count, itemContext) =>
            {
                itemContext.ReportProgress(index / (float)count, $"Downloading {Path.GetFileName(url)}");
                var target = Path.Combine("Library/ModelDownloads", Path.GetFileName(url));
                await downloader.DownloadFileAsync(url, target, null, null, itemContext.CancellationToken);
            },
            context);

        return true;
    }
}
```

## How the pieces fit

1. `StartOrResume` loads the saved queue, or creates it with the first callback
   and saves it.
2. The executor skips items whose keys are no longer in the queue, so a resumed
   queue continues where it stopped. Pass the full list every time.
3. Before each item, the executor saves the queue with that item as the current
   item. After the item, it removes the item's key and saves again.
4. When `RunAsync` returns `true`, the runner deletes the queue. Return `false`
   to keep it, for example after a failure the user should retry.

Item keys must be stable across reloads: use a URL, a path or another ID, not an
index.

## Cancellation

The **Background Tasks** cancel button and `Runner.Cancel()` cancel
`context.CancellationToken`. Pass it on to downloads and other async work, and
let the resulting `OperationCanceledException` through. The runner then deletes
the queue, so a cancelled operation does not resume, and calls your
`onCanceled` callback.

## Failures

If `RunAsync` throws, the runner calls `onUnhandledException` on the main thread
and keeps the queue. The next `StartOrResume`, or the next domain reload with
the code above, retries from the item that failed. Downloads in that item
resume from their partial files.
