# tools.doji.downloads

Reliable downloads of large files for Unity.

- `ResumableDownloader`: downloads a URL to a file, continues interrupted downloads with HTTP `Range`, and replaces the target only when the file is complete. Works in the Editor and in players.
- `FileLock`: keeps concurrent work, in this or another Editor, from writing the same files.
- `EditorPersistentOperationRunner` and `EditorPersistentItemQueueExecutor`: a queue of Editor work that survives domain reloads and Editor restarts, with a Background Tasks entry and a cancel button.

```csharp
await new ResumableDownloader().DownloadFileAsync(url, "Library/MyPackage/model.onnx", expectedSize, progress, cancellationToken);
```

Requires Unity 6000.3 LTS or later. Documentation: https://docs.doji-tech.com/tools.doji.downloads
