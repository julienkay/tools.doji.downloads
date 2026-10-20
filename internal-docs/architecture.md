# Architecture

## Assemblies

| Assembly | Namespace | Contents |
|---|---|---|
| `Doji.Downloads` | `Doji.Downloads` | `Runtime/Core`, `Runtime/Locking`, `Runtime/State` |
| `Doji.Downloads.Editor` | `Doji.Downloads.Editor` | `Editor/Operations`, `Editor/Transfer` |
| `Doji.Downloads.Editor.Tests` | `Doji.Downloads.Editor.Tests` | EditMode tests in `Tests/Editor` |

The companion project references the package with `file:` and lists it in
`testables`.

## Download

`UnityWebRequestResumableDownloader.DownloadFileAsync` writes to
`<target>.downloading` next to the target. When that file exists, it sends
`Range: bytes=<size>-` and appends. When the server answers `200` instead of
`206`, it ignores the range: the downloader deletes the partial file and
downloads again from zero, so bytes are not appended twice. Only a complete
download is moved over the target.

Requests are awaited with `Task.Yield` on the Unity main thread. A failed
request throws with the response code and error. Cancellation throws
`OperationCanceledException` and keeps the partial file for a later resume.

## State

`PersistentItemQueueState` holds the current item key and the pending item
keys, without duplicates. `JsonStateStore` saves it as JSON through a
temporary file, so an interrupted write keeps the old state. A missing or
invalid file loads as `null`, so the caller starts fresh.

`EditorPersistentItemQueueExecutor` processes items in order and saves the
state before and after each item. It processes every item it gets: the
caller passes only the items that are still pending. Item logic gets an index, a count and an
`EditorPersistentOperationContext`; it never saves or resumes state itself.

## Operations

`EditorPersistentOperationRunner` owns one state file and one Unity
`Progress` item. `StartOrResume` loads the saved state or creates a new one,
then runs the operation. When the operation returns `true`, the runner
deletes the state file; `false` keeps it for a later resume. After a domain
reload or an Editor restart, the caller checks `HasPersistedState` and calls
`StartOrResume` again with the same callback.

`EditorPersistentOperationContext` reports progress and queues work onto the
main thread.

## Locking

`FileLock.Acquire` opens a lock file with `FileShare.None` and retries every
100 ms until the timeout, so concurrent work does not write the same files.
Dispose releases it.

## Tests

Transfer tests run a local in-process HTTP server, with and without `Range`
support. Tests do not use the network.

## Known limits

- The downloader does not use `UnityEditor` but lives in `Editor`, so it is
  not available in players yet.
- The downloader does not check the final size against
  `expectedSizeInBytes` and does not take a `FileLock`.
- `Editor/AssemblyInfo.cs` grants `InternalsVisibleTo` to
  `Doji.ModelHub.Editor.Tests`, left over from extraction.
