# Architecture

## Assemblies

| Assembly | Namespace | Contents |
|---|---|---|
| `Doji.Downloads` | `Doji.Downloads` | `Runtime/Core`, `Runtime/Locking`, `Runtime/State`, `Runtime/Transfer` |
| `Doji.Downloads.Editor` | `Doji.Downloads.Editor` | `Editor/Operations` |
| `Doji.Downloads.Editor.Tests` | `Doji.Downloads.Editor.Tests` | EditMode tests in `Tests/Editor` |

The companion project references the package with `file:` and lists it in
`testables`.

## Download

`ResumableDownloader.DownloadFileAsync` writes bytes to
`<target>.downloading` and what the server said about the file (strong ETag
or `Last-Modified`, total size) to `<target>.downloading.json`. A partial
file without that info is deleted, because its bytes may belong to another
version of the file.

A resume sends `Range: bytes=<size>-` and `If-Range: <validator>`. The
handler decides from the response headers, before it writes a byte:

- `206` with a `Content-Range` that starts at the partial size and has the
  same total: append.
- `206` that starts elsewhere or has another total: discard the partial file
  and restart from zero once.
- `200`: the server ignored the range or the file changed. Truncate the
  partial file and write the body, in the same request.
- Not 2xx: write nothing. A `416` restarts from zero once; other errors throw
  `WebRequestException` and keep the partial file.
- A total that differs from `expectedSizeInBytes` fails before any write and
  deletes the partial file.

After the transfer, the partial size must equal the known total. A shorter
file throws and stays for the next resume; a longer one is deleted. Only then
does `File.Replace` (or `File.Move`) put it at the target. A partial file
that already has the full size is moved without a request.

Requests ask for `Accept-Encoding: identity`, because compressed bodies do
not match byte ranges. `UnityWebRequestExtensions.SendAsync` awaits
`AsyncOperation.completed` instead of polling. Cancellation aborts the
request on the thread that sent it and throws `OperationCanceledException`;
the partial file stays.

Progress is reported when the response is accepted, at most every 100 ms
while data arrives, and once when the body is complete.

## State

`PersistentItemQueueState` holds the current item key and the pending item
keys, without duplicates. `JsonStateStore` writes JSON to a temporary file and
swaps it in with `File.Replace`, so an interrupted write keeps the old state.
A missing file or invalid JSON loads as `null`, so the caller starts fresh.
A file that cannot be read (for example, held by another process) throws, so
good state is never overwritten by a fresh one.

`EditorPersistentItemQueueExecutor` processes items in order and saves the
state before and after each item. It skips items whose keys are no longer
pending, so callers pass the full item list on every resume. It checks
cancellation before each item. Item logic gets an index, a count and an
`EditorPersistentOperationContext`; it never saves or resumes state itself.

## Operations

`EditorPersistentOperationRunner` owns one state file and one Unity
`Progress` item. It creates the item on start, ignores progress reports that
arrive after the operation ended, and removes the item when the operation
ends in any way. `StartOrResume` loads the saved state or creates a new one,
then runs the operation. When the operation returns `true`, the runner
deletes the state file; `false` keeps it for a later resume. After a domain
reload or an Editor restart, the caller checks `HasPersistedState` and calls
`StartOrResume` again with the same callback.

`Cancel` and the cancel button in Background Tasks cancel the context's
token. When the operation ends with `OperationCanceledException`, the runner
deletes the state file, so a cancelled operation does not resume, and calls
`onCanceled` on the main thread. Item logic must let that exception through.

`EditorPersistentOperationContext` reports progress and queues work onto the
main thread.

## Locking

`FileLock.TryAcquire` opens a lock file with `FileShare.None`.
`FileLock.AcquireAsync` retries every 100 ms with `Task.Delay` until the
timeout or cancellation, so waiting never blocks the main thread. Dispose
releases the lock. The downloader does not lock; callers that can download
the same target concurrently hold a lock around it.

## Tests

Transfer tests run a local in-process HTTP server that can ignore `Range`,
honor `If-Range`, return errors, cut a response or hold it open. Tests do not
use the network.

## Known limits

- The downloader does not check content hashes; callers that have one check
  the target after the download.
