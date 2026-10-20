# Getting started

## Installation

Download Utils is published on OpenUPM. Add a scoped registry for `tools.doji`
to `Packages/manifest.json`, then add the package:

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [ "tools.doji" ]
    }
  ],
  "dependencies": {
    "tools.doji.downloads": "1.0.0"
  }
}
```

Download Utils requires Unity 6000.3 LTS or later. Its only dependency,
Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`), is installed by Package
Manager.

If you build a package on top of Download Utils, add `tools.doji.downloads` to
the `dependencies` of your own `package.json` instead.

## Your first download

`ResumableDownloader` downloads one URL to one file. Call it from the Unity main
thread, for example from an Editor menu item:

```csharp
using System;
using System.Threading;
using Doji.Downloads;
using UnityEditor;
using UnityEngine;

static class DownloadExample
{
    [MenuItem("Tools/Download Example")]
    static async void Download()
    {
        var progress = new Progress<DownloadProgress>(value =>
            Debug.Log($"{value.DownloadedBytes} of {value.TotalBytes} bytes"));

        try
        {
            await new ResumableDownloader().DownloadFileAsync(
                "https://example.com/model.onnx",
                "Library/DownloadExample/model.onnx",
                expectedSizeInBytes: null,
                progress,
                CancellationToken.None);
            Debug.Log("Done");
        }
        catch (Exception exception)
        {
            Debug.LogError(exception.Message);
        }
    }
}
```

While the file downloads, two extra files sit next to the target:

- `model.onnx.downloading`: the bytes received so far
- `model.onnx.downloading.json`: what the server said about the file, used to
  resume safely

Stop the download, or disconnect the network, and run the menu item again: the
download continues where it stopped. When it is complete, both files are
replaced by `model.onnx`.

> [!TIP]
> Download into `Library/` or a folder outside `Assets/`. Inside `Assets/`,
> Unity imports the partial files while they grow.

## Next steps

- [Downloading files](downloading-files.md): sizes, errors, cancellation and
  locks.
- [Persistent Editor operations](editor-operations.md): download a list of
  files in the background and continue after a domain reload.
