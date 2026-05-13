# Cross-platform validation screenshots

The four PNGs referenced by [`docs/cross-platform.md`](../../cross-platform.md)
go in this folder. Drop the images at the exact filenames below — Jekyll
will pick them up via `{{ site.baseurl }}/assets/cross-platform/<name>.png`.

| Filename | What the screenshot should show |
|---|---|
| `01-geometry-selftest-wsl2.png` | A WSL2 terminal after running `dotnet ... --geometry-selftest`, showing the 19 PASS lines and the final `OK — 19/19 geometry self-tests passed.` |
| `02-iogp-selftest-wsl2.png` | A WSL2 terminal after running `dotnet ... --iogp-selftest`, showing the equipment-type PASS lines and the final `Total: 27 passed, 0 failed.` |
| `03-list-gpus-wsl2.png` | A WSL2 terminal after running `dotnet ... --list-gpus`, showing the FluidX3D banner, the device table, and the resulting JSON `[{"id":0,"name":"...",...}]` |
| `04-avalonia-all-green-wsl2.png` | The `DisperSim3D.UI.Avalonia` window rendered via WSLg on the Windows desktop with all four panels populated in **green** text — geometry, IOGP, OpenCL JSON, and the Gaussian plume `MaxC` result |

## How to capture them

On WSL2:

```bash
# Terminal 1 — produce the three CLI outputs
cd /mnt/c/Users/<you>/source/repos/DanWBR/DisperSim\ 3D
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --geometry-selftest
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --iogp-selftest
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --list-gpus
```

Snip each one (Win+Shift+S on Windows 11 captures the WSL terminal area)
and save as the three CLI filenames above.

For the Avalonia window:

```bash
dotnet DisperSim3D.UI.Avalonia/bin/Release/net10.0/DisperSim3D.UI.Avalonia.dll
```

Click each of the four `Run`/`Probe` buttons in order. Once all four
panels show green text, snip the whole window.

## Format notes

- **PNG**, lossless. JPG compresses terminal text badly.
- **Width**: any — the docs page uses default Markdown image rendering so
  big screenshots scale to the column width via the Just-the-Docs theme.
- **Dark background OK** — the terminal screenshots look better with the
  default WSL/Windows Terminal dark scheme.
- Filenames are case-sensitive on Linux/macOS Jekyll builds. Use the exact
  names in the table above.
