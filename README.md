# PicoGK reproductions

Three small reproductions against PicoGK 2.3 (main 0e6cf6b), PicoGKRuntime main 0f26321 and LEAP71_ShapeKernel main 7b90978, each checked against a closed formula on a sphere.

| mode | what it shows |
|---|---|
| `traegheit <voxel_mm>` | `Measure.matGetMomentOfInertia` (ShapeKernel) weighs only the narrow band: +25 % at 5 mm, +57 % at 1 mm on a sphere R = 50 mm. Same loop without the `bGetValue` return value as filter: −0.14 %. |
| `innen <voxel_mm>` | `Voxels.bIsInside` returns true for every point, at every voxel size. |
| `innen2 <voxel_mm>` | Same native function called with `[return: MarshalAs(UnmanagedType.I1)]` and with the raw return register. Shows the cause: a one byte C++ `bool` read as a four byte BOOL. |
| `volumen <voxel_mm>` | `Measure.fGetVolume` returns the outer volume for a closed hollow sphere (+14.26 %); correct once a bore opens the cavity. |
| `regress <voxel_mm>` | Regression check for `Voxels.bIsInside`, after the test by @herbstritt in [PR 115](https://github.com/leap71/PicoGK/pull/115#issuecomment-6641886357): sphere r = 10 mm from a `Lattice`, five points, PicoGK only. Prints PASS or FAIL, exit code 0 or 1. Use `regress 0.25`. |

## Build and run

Expected layout: this folder next to `PicoGK` and `LEAP71_ShapeKernel` (the csproj references them by relative path).

```
dotnet build -c Release
dotnet run -c Release --no-build -- traegheit 1
dotnet run -c Release --no-build -- innen 5
dotnet run -c Release --no-build -- innen2 5
dotnet run -c Release --no-build -- volumen 1
dotnet run -c Release --no-build -- regress 0.25
```

To build against another PicoGK checkout, for example the PR branch: `dotnet build -c Release -p:PicoGKDir=../PicoGK_pr115`.

On a headless Linux machine: copy the runtime next to the binary as `libpicogk.26.2.so` and run under `xvfb-run -a`. `lauf.sh` does that for my setup (paths are mine, adjust them).

`ergebnis_2026-09-25_*.txt` are the raw outputs from 2026-09-25, Ubuntu 25.10, .NET 9.0.118, runtime built from source.

`ergebnis_2026-11-05_regress.txt`: `regress 0.25` against PicoGK main 0e6cf6b and PR 115 head e2bbbb6. On Linux x86-64 (runtime built from source) main fails 3 of 5 points and the PR passes. On macOS arm64 (runtime from `PicoGK/native/osx-arm64`) both pass, so this build does not show the bug, like the Windows x64 result in the PR.

Daniel Leonforte, Wiesbaden
