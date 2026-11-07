# Ready Windows demo

Open [windows-x64/ImageBatchDesk.exe](windows-x64/ImageBatchDesk.exe) on Windows x64. No installation, .NET SDK, account, internet or development dependencies are required. Keep `samples/` beside the executable to use **Try samples**.

1. Select **Try samples** to load four original geometric images.
2. Set maximum edge to `1200`, format to `JPEG` and suffix to `small`.
3. Choose an existing empty output folder outside `samples/`.
4. Select **Process batch**. Expect four copies; the 2400 × 1600 landscape becomes 1200 × 800. The small 320 × 320 image keeps its size.
5. Run again to see numbered copies. Try PNG to keep transparency. Cancel a larger batch to stop after the current codec operation, preserving finished copies.

The program is unsigned. First launch decompresses the included .NET runtime into the Windows temporary area. It writes images only to the chosen destination and holds its queue/settings in memory. It makes no network requests. Close the app before replacing its EXE.

[SHA256SUMS.txt](SHA256SUMS.txt) inventories all demo files except the inventory itself. To check the executable in PowerShell:

```powershell
Get-FileHash windows-x64/ImageBatchDesk.exe -Algorithm SHA256
```

See [limits and verification](../docs/VERIFICATION.md). OS file-dialog and injected keyboard/mouse testing were unavailable; the packaged in-process workflow and actual WPF renders were checked.

Source and original samples: [MIT](../LICENSE). Included runtime licenses are in [windows-x64/licenses/](windows-x64/licenses/).
