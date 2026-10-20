# Downloading files

`ResumableDownloader.DownloadFileAsync` downloads one URL to one file. It works
in the Editor and in players.

## How resuming works

The downloader writes to `<target>.downloading`. When that file already exists,
it asks the server only for the missing bytes, with an HTTP `Range` request.

The downloader resumes only when it can prove the bytes on disk belong to the
same file. When a download starts, it saves the server's `ETag` or
`Last-Modified` value and the file size next to the partial file. A resume sends
that value back (`If-Range`), so the server returns the whole file when it has
changed. The downloader then starts over in the same request.

It also starts over, at most once, when:

- the server ignores `Range` and sends the whole file
- the server rejects the range (`416`), for example because the partial file
  is longer than the remote file
- the server returns another range or another total size than expected
- a partial file has no saved server info, for example from another tool

A partial file that already has the full size is moved into place without a
request.

> [!NOTE]
> Servers that support neither `Range` nor `ETag`/`Last-Modified` still work,
> but every attempt downloads the whole file.

## The target is replaced only when complete

The target file changes only after a complete download. Before that, the
downloader checks the received size against the size the server announced and
against `expectedSizeInBytes`:

| Result | Target | Partial file |
|---|---|---|
| Complete | Replaced | Removed |
| Too short, for example a dropped connection | Unchanged | Kept, the next call resumes |
| Too long, or the server reports a different size than `expectedSizeInBytes` | Unchanged | Deleted |

Pass `expectedSizeInBytes` when you know the size: a wrong size then fails
before any byte is written.

The downloader does not check content hashes. If you have one, check the
target file after the download.

## Errors

| Exception | When | Partial file |
|---|---|---|
| `WebRequestException` | The connection failed or the server returned an error. `Url` and `ResponseCode` say which request and why. | Kept |
| `IOException` | The download is incomplete or has the wrong size. The message names the URL and both sizes. | Kept if too short, deleted otherwise |
| `OperationCanceledException` | The `CancellationToken` was cancelled. | Kept |

Error responses are never written to the partial file. To retry, call
`DownloadFileAsync` again.

## Progress

`progress` receives a `DownloadProgress` with `DownloadedBytes` (including bytes
from earlier attempts) and `TotalBytes` (null when the server does not say).
It is reported on the calling thread when the download starts, at most every
100 ms while it runs, and once at the end.

## Cancellation

Cancel the token to stop a download. The request is aborted and the partial
file stays, so a later call resumes.

## Avoid two downloads of the same file

Two downloads into the same target, in one Editor or in two, must not run at
the same time. Hold a `FileLock` around the download:

```csharp
using var fileLock = await FileLock.AcquireAsync(
    targetPath + ".lock",
    TimeSpan.FromSeconds(30),
    cancellationToken);
await new ResumableDownloader().DownloadFileAsync(url, targetPath, size, progress, cancellationToken);
```

`AcquireAsync` waits without blocking the main thread and throws
`TimeoutException` when the lock stays taken. `FileLock.TryAcquire` takes the
lock only if it is free. Disposing the lock releases it.

## Other requests

`UnityWebRequestExtensions.SendAsync` awaits any `UnityWebRequest`, for example
a `HEAD` request for a file size:

```csharp
using var request = UnityWebRequest.Head(url);
await request.SendAsync(cancellationToken);
var size = request.GetResponseHeader("Content-Length");
```

It throws `WebRequestException` for failed requests and
`OperationCanceledException` after cancellation.
