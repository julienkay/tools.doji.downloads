# Download Utils

## Purpose

Download Utils downloads large files in Unity reliably. A download continues
after a network failure, a domain reload, or an Editor restart, and never
leaves a broken file that looks complete.

It is generic infrastructure. It knows nothing about HuggingFace, AI models,
or Sentis. `tools.doji.model-hub` is its first user.

## Users

Package developers who must fetch files that are too large to ship: AI
models, datasets, media, or other remote content. They use Download Utils so
they do not write their own resume, retry, and state code.

## Principles

- **Resume, do not restart.** Write to a partial file next to the target and
  continue with HTTP `Range` requests. Replace the target only when the
  download is complete.
- **Survive Unity.** Keep queue state on disk, so work continues after a
  domain reload or an Editor restart. Item logic must not handle save and
  resume itself.
- **Never corrupt.** Lock files that concurrent work can touch. A failed or
  cancelled download must not leave a partial result marked complete.
- **Stay generic.** No knowledge of a specific host, file format, or caller.
  Callers own their URLs, cache layout, and validation.
- **Runtime-safe core.** Keep state, locking, and progress in `Runtime`.
  Only Unity Editor integration (`Progress` UI, Editor update loop) belongs
  in `Editor`. Runtime download support reuses the same core.
- **Show progress and errors.** Report bytes and items done. Errors name the
  URL and the cause.
- **Few dependencies.** Unity and Newtonsoft JSON only.

## Out of scope

- Host-specific APIs, authentication flows, or cloud storage SDKs.
- Unpacking archives or converting downloaded files.
- Peer-to-peer or parallel chunked downloads of one file.

## Decision test

Does the change make large downloads in Unity more reliable or easier to use,
without tying the package to one caller? If not, do not make the change.
