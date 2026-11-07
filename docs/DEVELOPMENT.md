# Build and design

## Requirements

Windows x64, .NET SDK 10.0.302 (see [global.json](../global.json)). WPF and Windows Imaging Component provide the codecs and desktop UI. There are no third-party application packages. Restore lockfiles include SDK publishing tooling.

```powershell
dotnet restore ImageBatchDesk.csproj --locked-mode
dotnet restore tests/EngineTests.csproj --locked-mode
dotnet format ImageBatchDesk.csproj --verify-no-changes --no-restore
dotnet run --project tests/EngineTests.csproj -c Release --no-restore
dotnet publish ImageBatchDesk.csproj -c Release --no-restore -o demo/windows-x64
python scripts/inventory.py
```

Publishing replaces the executable; close the demo first. Preserve the checked-in samples, runtime license files and demo launch instructions. Python 3 is needed only to regenerate the checksum inventory. Pillow is optional for regenerating original samples or the icon (`scripts/make-samples.py`, `scripts/make-icon.py`); the checked-in assets require neither Python nor Pillow to run. Sample generation uses the locally installed Segoe UI fonts and redistributes no font binaries.

## State and execution

The window owns one observable queue, the destination, settings and current batch plan. Inspection produces immutable source information and a frozen thumbnail capped at 360 pixels. Settings validation produces immutable output paths/dimensions before work starts. Filesystem names are checked again on each run. Naming follows queue order; case-insensitive matching covers both existing files/directories and duplicates in the same batch.

Decoding, rendering and encoding execute serially on an STA worker; WPF frozen images can cross thread boundaries. The dispatcher remains available for progress and cancellation. Settings and queue mutation lock during import/processing. Closing during an operation requests cancellation and keeps the window open until a second close after completion. Import itself is bounded but has no cancellation control.

The decoder checks file size, codec, frame count and dimensions before copying pixels. A PNG chunk scan rejects animation controls. EXIF transforms precede aspect-ratio fitting. WPF's high-quality raster renderer creates a 96-DPI BGRA output, adding white only for JPEG. New encoders receive pixels without source metadata.

Each image is encoded into an exclusively created temporary file in the destination, flushed, checked for cancellation, then moved without replacement. Failure affects that image; later inputs continue. No original or previously existing output is deleted. A concurrent collision is reported; it is never silently renamed after the displayed plan. On a crash, temporary files may remain. Directory aliases and external source modification are not snapshot-isolated; dimensions are checked again before export.

## Diagnostic mode

The **exact packaged executable** supports:

```powershell
demo/windows-x64/ImageBatchDesk.exe --self-test C:/Temp/image-desk-check-new
```

Supply a directory that does not exist. The command creates synthetic test copies, outputs, `result.json` (or `failure.txt`) and ten PNG renders. It shows a real WPF window and exercises the same import, plan, conversion and cancellation workflows in-process. It does not inject OS input or open native file dialogs. Exit code is zero on success. The test data remains in the supplied directory for inspection; it never cleans unrelated folders. Keep `samples/` beside the EXE.

## References

- [WPF imaging overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/imaging-overview)
- [.NET deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
