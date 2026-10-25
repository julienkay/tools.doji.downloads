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

`ResumableDownloader.DownloadFileAsync` resolves the target to a full path,
writes bytes to `<target>.downloading`, and writes the source URL and what the
server said about the file (strong ETag or `Last-Modified`, total size) to
`<target>.downloading.json`. A partial file is deleted when that info is
missing, when the URL differs, or when it is incomplete and has no validator,
because its bytes may belong to another file or another version of it.

A resume sends `Range: bytes=<size>-` and `If-Range: <validator>`. Each
request writes its body to `<target>.downloading.segment` with Unity's
`DownloadHandlerFile`, so bytes never pass through managed code and the
download runs at disk and network speed. The downloader polls the request each
frame. As soon as the status code is known, it decides from the headers:

- `206` with a `Content-Range` that starts at the partial size and has the
  same total: append the segment to the partial file.
- `206` that starts elsewhere or has another total: abort, discard the
  partial file and restart from zero once.
- `200`: the server ignored the range or the file changed. The segment
  replaces the partial file.
- `206` to a request without `Range`: the segment becomes a new partial file
  with its announced total, so the final size check rejects a body that
  covers only part of the file.
- Not 2xx: drop the segment. A `416` restarts from zero once; other errors
  throw `WebRequestException` and keep the partial file.
- A total that differs from `expectedSizeInBytes`: abort, delete the partial
  file and throw `IOException`.

The segment joins the partial file after the request ends, also when the
connection dropped or the call was cancelled after the response was
accepted, so the next call resumes after the bytes received. When the
segment replaces the partial file, the old info is deleted first and the new
info is saved after the bytes, so old bytes never pair with a new validator.
A leftover segment from an interrupted process is deleted before the next
request.

After the transfer, the partial size must equal the known total. A shorter
file throws and stays for the next resume; a longer one is deleted. Only then
does `File.Replace` (or `File.Move`) put it at the target. A partial file
from the same URL that already has the full size is moved without a request.

Requests ask for `Accept-Encoding: identity`, because compressed bodies do
not match byte ranges. Cancellation aborts the request and throws
`OperationCanceledException`; the bytes received stay in the partial file.
`UnityWebRequestExtensions.SendAsync` is for other requests: it awaits
`AsyncOperation.completed` instead of polling.

Progress is reported when the response is accepted, at most every 100 ms
while data arrives, and once when the request ends.

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
