# FolderSync

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A modern, maintained replacement for SyncToy's folder-pair sync model, built for
Windows 11 on .NET 8. This first pass is the core engine plus a CLI — no GUI yet.

## Project layout

```
FolderSync.sln
SyncEngine/           <- UI-agnostic core library
  SyncMode.cs          Echo / Sync / Contribute
  FolderPair.cs         a configured pair of folders + mode + excludes
  FileState.cs           one file's size/timestamp/hash
  Scanner.cs               walks a folder into a FileState map
  StateStore.cs             JSON snapshot of last-known state, per pair
  SyncAction.cs               one planned change (copy/delete/conflict)
  DiffEngine.cs                 current state + last snapshot -> plan
  ActionExecutor.cs               applies a plan to disk
SyncEngine.Cli/        <- console front-end, also callable from Task Scheduler
  Program.cs
SyncEngine.Tests/      <- xUnit tests for the core engine (DiffEngine so far)
  TestData.cs            terse builders for FileState/snapshots/pairs
  DiffEngineTests.cs      Echo / Contribute / Sync coverage
  ScannerFilesDifferTests.cs  real-disk tests for hash-based conflict checks
SyncEngine.Gui/        <- WinUI 3 desktop app
  MainWindow.xaml(.cs)    bare Window; hosts MainView as its Content in code-behind
  MainView.xaml(.cs)       the actual UI (pairs list, detail form, preview/run, action list) —
                            lives in a UserControl, not directly on Window, because Window
                            doesn't derive from FrameworkElement and can't host x:Bind or
                            a Resources dictionary the way a Page/UserControl can
  MainViewModel.cs         owns the pairs collection, persistence, preview/run
  FolderPairViewModel.cs    editable wrapper around a FolderPair
  ActionRow.cs               display wrapper around a SyncAction
  ObservableObject.cs         small INotifyPropertyChanged base
  RelayCommand.cs               small ICommand for button bindings
```

## How the sync decision works

Each run:
1. **Scan** both folders (`Scanner`) — fast size+timestamp comparison, no hashing by default.
2. **Load** the last-known state of both sides from `pairs.json`'s companion snapshot (`StateStore`).
3. **Diff** current vs. last-known state to classify every file as new / modified /
   deleted / unchanged on each side (`DiffEngine`).
4. Apply mode rules:
   - **Echo** — left is master, right mirrors it exactly, including deletes.
   - **Contribute** — same as Echo but never deletes on the right.
   - **Sync** — changes propagate whichever direction they happened; a file changed
     on *both* sides since the last run is checked against a SHA-256 hash of both
     copies before being flagged as a **Conflict** — a size/timestamp mismatch
     alone isn't proof the content actually differs, and a match alone isn't proof
     it doesn't.
5. **Preview** shows the plan without touching disk. **Run** applies it, then saves
   a fresh snapshot so next time's diff is accurate.

## Installing via the installer (GUI, no build required)

The `FsyncInstaller` project packages the GUI into a per-user MSI — no admin
rights or UAC prompt needed. The MSI itself isn't checked into the repo (it's
gitignored build output), so build it locally first:

```powershell
cd FolderSync\FsyncInstaller
.\build.ps1
```

This requires the Windows App SDK workload (see "Building & running the GUI"
below) plus the `wix` global tool:

```powershell
dotnet tool install --global wix --version 5.0.2
wix extension add WixToolset.UI.wixext/5.0.2 --global
```

`build.ps1` publishes a self-contained `SyncEngine.Gui` build and produces
`FsyncInstaller\FolderSyncSetup.msi`. Run that MSI (double-click it, or
`msiexec /i FolderSyncSetup.msi`) and step through the install wizard — accept
the license, pick an install directory (defaults to
`%LocalAppData%\Programs\FolderSync`), and it adds Desktop and Start Menu
shortcuts.

To reinstall after rebuilding, bump `$Version` in `build.ps1` (or pass
`-Version X.Y.Z.W`) above whatever's currently installed — Windows Installer
treats an equal-or-lower version as a downgrade and blocks it. Check what's
currently installed with:

```powershell
$installer = New-Object -ComObject WindowsInstaller.Installer
$installer.RelatedProducts("{A325883B-D61C-43A6-B2DC-CC0D59E32954}") |
    ForEach-Object { $installer.ProductInfo($_, "VersionString") }
```

## Build & run (requires .NET 8 SDK)

```powershell
cd FolderSync
dotnet build

cd SyncEngine.Cli
dotnet run -- add Documents "C:\Users\you\Documents" "D:\Backup\Documents" Sync
dotnet run -- preview Documents
dotnet run -- run Documents
```

`pairs.json` and the state snapshots are created automatically next to the CLI
executable. Point Windows Task Scheduler at `sync.exe run <pairName>` for
scheduled syncing (this is essentially how SyncToy's own scheduling worked).

## Running the tests

```powershell
cd FolderSync
dotnet test
```

`DiffEngineTests` covers all three modes against the scenarios that matter most:
new/modified/deleted files on one side, unchanged files, files deleted on both
sides since the last run (no-op), both delete-vs-modify combinations (always a
conflict — there's no file left to hash, and a delete and an edit are genuinely
different intents), and the hash-comparer wiring itself: cases where the quick
size/timestamp check and the hash comparer disagree, confirming the hash wins,
plus a check that the comparer is never invoked unless both sides actually
changed (it does real disk I/O in production, so it shouldn't run needlessly).
`DiffEngineTests` stays pure/in-memory throughout — the comparer is a fake
delegate, not real hashing — so it runs in milliseconds. `ScannerFilesDifferTests`
separately exercises the real `Scanner.FilesDiffer` against actual temp files on
disk, including the case QuickEquals can't catch: identical size and timestamp
with genuinely different content.

## Building & running the GUI

The GUI needs the **Windows App SDK** workload, which requires Visual Studio 2022
(the ".NET Desktop Development" + "Windows application development" workloads)
or the standalone Windows App SDK tooling — it's not something `dotnet build`
alone will have everything for on a fresh machine.

```powershell
cd FolderSync
dotnet build SyncEngine.Gui\SyncEngine.Gui.csproj -r win-x64
```

Or just open `FolderSync.sln` in Visual Studio and run `SyncEngine.Gui` — that's
the more reliable path the first time, since VS will offer to install any
missing Windows App SDK components for you.

The GUI reads and writes the **same** `pairs.json` and state snapshots as the
CLI (`SyncEngine.Cli`), so a pair added in one shows up in the other — they're
two front-ends over the same engine and the same on-disk state, not separate
tools. The window has a pairs list on the left; selecting a pair shows an
editable name/paths/mode form, a Preview/Run pair of buttons, a table of
planned changes (with conflicts visually flagged), and a status line. Preview
and Run both use the hash-backed conflict check described above, run off the
UI thread so the window doesn't freeze during a scan.

## Known gaps / next steps

- **Conflict resolution** — Sync-mode conflicts are confirmed by hash but still
  just reported and skipped, not resolved. Worth adding a
  `--resolve left|right|newer` flag or interactive prompt (CLI), and a
  resolve-per-row action in the GUI's plan list.
- **Long path support** — add the `\\?\` prefix handling for paths beyond 260 chars.
- **GUI polish** — no run history, no app icon/packaging (MSIX) yet, no way to
  cancel an in-progress scan, and the conflict rows in the plan list are just
  displayed, not actionable. Also worth surfacing `ActionExecutor`'s per-file
  errors somewhere more visible than the status line.
- **Real-time mode** — a `FileSystemWatcher`-based mode that debounces changes and
  triggers a mini-sync, as an alternative to scheduled/manual runs.
- **Symlinks/junctions** — currently untested; decide whether to follow or skip them.
