# Download Utils

Download Utils downloads large files in Unity reliably. A download continues
where it stopped after a network failure, a domain reload or an Editor restart,
and it never leaves a broken file that looks complete.

It is for package and project developers who must fetch files that are too
large to ship: AI models, datasets, media or other remote content.

## What you get

| | Feature | Where |
|:-:|---|---|
| ⬇️ | **[Resumable downloads](manual/downloading-files.md)**: continue with HTTP `Range`, replace the target only when the file is complete | Editor and players |
| 🔒 | **[File locks](manual/downloading-files.md#avoid-two-downloads-of-the-same-file)**: keep two Editors from writing the same file | Editor and players |
| 🔁 | **[Persistent Editor operations](manual/editor-operations.md)**: a queue of work that survives domain reloads, with a Background Tasks entry and a cancel button | Editor |

## Download a file

```csharp
using System.Threading;
using Doji.Downloads;

var downloader = new ResumableDownloader();
await downloader.DownloadFileAsync(
    "https://example.com/model.onnx",
    "Library/MyPackage/model.onnx",
    expectedSizeInBytes: null,
    progress: null,
    CancellationToken.None);
```

If the download fails, call it again: it continues from the bytes already on
disk. [Getting started](manual/getting-started.md) explains installation and
the first download.

The [Scripting API](api/index.md) describes every type and member.
