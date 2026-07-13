# ViewPrism2

A Windows desktop image-management application built on **.NET 10**, **Avalonia UI 12**, and **SQLite**.

Its central idea is **tags × virtual views**: instead of being bound to the physical folder tree, you tag images and then browse them through user-defined *virtual* hierarchies ("views") that behave like folders. The same picture can live in as many views as you like, and nothing is ever moved or copied — image files stay exactly where they are on disk, and the database only records references and metadata.

> ViewPrism2 is a .NET/Avalonia/SQLite re-implementation of the original *view-prism* (TypeScript/Electron) application.

## Highlights

- **Non-destructive by design** — images are discovered in place; no import copies, and merges/deletes are logical (physical files are never touched).
- **Tags × virtual views** — browse tagged images through savable virtual folder hierarchies, independent of where files physically live.
- **Perceptual near-duplicate detection** — DCT-based pHash + Hamming distance to find visually similar images and merge duplicates.
- **Rename- and move-resistant** — content hashing (SHA-256) detects moved/renamed files and offers to re-link them without losing tags.
- **Fast on large libraries** — virtualized image grids and viewer, background scanning with progressive publish, precomputed sort keys.

## Features

| Area | What it does |
|---|---|
| **Sync folders & scanning** | Register physical folders as scan roots. Incremental scans (SHA-256) classify each file as `normal` / `missing` / `pending`, detecting renames within a single pass. |
| **Tag system** | Three tag kinds — *simple* (no value), *textual* (string, with predefined values), *numeric* (min/max/step/unit). Tags form a single-parent hierarchy. |
| **Virtual views** | A view bundles filter conditions, sort order, display columns, and a tag hierarchy into a savable virtual folder. |
| **Tag hierarchy & NodeGraph** | Define a tag tree per view; the app expands it against actual tagging into a navigable node graph. |
| **Thumbnails & image display** | SkiaSharp-based decode/resize with EXIF-orientation handling on the display path. |
| **Viewer modes** | `normal` (single), `scroll` (continuous vertical), and `spread-right` / `spread-left` (two-page spreads) with keyboard navigation. |
| **Similarity search & merge** | Find near-duplicates of a reference image by pHash similarity, then merge — tags are consolidated and the source is logically deleted. |
| **Repair lifecycle** | Re-link `missing` images to newly discovered `pending` files, preserving tags and notes. |
| **Backup / restore** | Catalog DB snapshots and a portable collection package format for moving a library between machines. |
| **i18n & settings** | Japanese / English localization; window state and preferences persisted to `settings.json`. |

## Tech stack

- **Runtime**: .NET 10, Windows desktop (`WinExe`)
- **UI**: Avalonia UI 12 (Fluent theme), CommunityToolkit.Mvvm (MVVM)
- **Data**: SQLite via Microsoft.Data.Sqlite + Dapper (WAL, foreign keys on)
- **Imaging**: SkiaSharp (decode / resize / pHash)
- **Logging**: Serilog
- **Tests**: xUnit v3 (unit tests + a golden/oracle suite)

## Project layout

```
src/
  ViewPrism2.App             Avalonia UI — Views, ViewModels, Controls, Styles, Services
  ViewPrism2.Core            Domain models, repositories (interfaces), core services (pHash, similarity, repair)
  ViewPrism2.Infrastructure  SQLite database, imaging, scanning, i18n, settings
tests/
  ViewPrism2.Tests           Unit / control-plan tests
  ViewPrism2.Oracle          Frozen oracle (golden) tests
  ViewPrism2.GoldenHarness   Headless visual/golden harness
bomdd/                       Design, acceptance, and change-management ledger (see below)
```

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows (the app targets the Windows desktop)

### Build & run

```bash
# restore & build
dotnet build

# run the app
dotnet run --project src/ViewPrism2.App

# run the tests
dotnet test tests/ViewPrism2.Tests
dotnet test tests/ViewPrism2.Oracle
```

On first launch the database is created at `%APPDATA%/ViewPrism2/data/viewprism.db`.

## Development methodology

ViewPrism2 is manufactured with **BomDD** (BOM-Driven Development). The `bomdd/` directory is the authoritative ledger for design, acceptance criteria, and change management — every change to `src/`/`tests/` originates from a registered ECO (Engineering Change Order). The UI/UX design authority (CAD) lives in a separate `ViewPrismUI` repository. See [`CLAUDE.md`](CLAUDE.md) and [`bomdd/change-management.md`](bomdd/change-management.md) for the workflow.

## License

Released under the [MIT License](LICENSE). Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
