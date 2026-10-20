# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-20

### Added

- Initial release.
- `ResumableDownloader`: resumable file downloads with `Range` and `If-Range`, size checks before the target is replaced, and one restart from zero when a partial file cannot continue. Runtime-safe.
- `UnityWebRequestExtensions.SendAsync` and `WebRequestException`, which names the URL and the cause.
- `FileLock.TryAcquire` and `FileLock.AcquireAsync`, which waits without blocking the main thread.
- `EditorPersistentOperationRunner` and `EditorPersistentItemQueueExecutor`: persistent Editor operations that resume after a domain reload, with Background Tasks progress and cancellation.