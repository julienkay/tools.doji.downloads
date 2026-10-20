# AGENTS.md

## Project Intent

This repository contains the Unity package `tools.doji.downloads` and its companion project `Download Utils`.

Asynchronous, resumable background downloads of large files for Unity.

Unity 6000.6 for development; the package supports 6000.3 LTS and later.

## Package Direction

- Follow `VISION.md` for durable product direction, scope and the decision test.
- Stay generic: no host-, format- or caller-specific code. `tools.doji.hf-hub` is the first user, not a dependency.
- Keep `Runtime` free of `UnityEditor`; only Editor integration belongs in `Editor`.
- Depend only on Unity modules and Newtonsoft JSON.
- The package is in active development. Prefer the current design over backwards compatibility.
- Keep documentation about the current design and behavior. Update it with the code; remove temporary notes once the work they track is done.

## Source Of Truth

Prefer these locations:

- repository root: orient from the repository root for searches, docs, and cross-package work; from this companion Unity project working directory, the repository root is `../..`
- `tools.doji.downloads/`: local package under development
  - `Runtime`: resumable downloads (`Transfer`), state, locking and progress (`ResumableDownloader`, `JsonStateStore`, `PersistentItemQueueState`, `FileLock`, `DownloadProgress`)
  - `Editor`: persistent operations with Unity `Progress` UI and cancellation (`Operations`)
  - `Tests/Editor`: EditMode tests, grouped like the package folders
- `docs/`: user-facing package documentation only; keep maintainer notes and internal architecture docs out of DocFX content
- `internal-docs/`: design notes for maintainers; `internal-docs/README.md` indexes them. Read
  `internal-docs/architecture.md` before a change to download, state, locking or operation flow.
- `projects/Download Utils/Assets`: companion-project assets and test scenes

Prefer working in the package folder when functionality is package-owned.

## Conventions

When adding code:

- organize scripts by task area; prefer colocated or clearly scoped folders such as docs-, tooling-, or workflow-specific script locations over a shared miscellaneous scripts bucket
