# Download Utils

Full documentation: https://docs.doji-tech.com/tools.doji.downloads/

## Overview

Asynchronous, resumable background downloads of large files. An interrupted download continues where it stopped, and the target file is replaced only when the download is complete.

## Requirements

Unity 6000.3 or newer. Depends on `com.unity.nuget.newtonsoft-json`. The runtime API works in the Editor and in player builds.

## Usage

- `ResumableDownloader.DownloadFileAsync` downloads a URL to a file and resumes interrupted downloads.
- `FileLock` keeps concurrent work, also from another Editor, from writing the same files.
- `EditorPersistentOperationRunner` and `EditorPersistentItemQueueExecutor` run Editor work that survives domain reloads and Editor restarts, with progress and cancellation in the Background Tasks window.

```csharp
await new ResumableDownloader().DownloadFileAsync(url, "Library/MyPackage/model.onnx", expectedSize, progress, cancellationToken);
```

## Support

support@doji-tech.com
