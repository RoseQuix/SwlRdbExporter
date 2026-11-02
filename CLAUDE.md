# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 5 console tool (`RdbExporter`) that extracts assets from Secret World Legends (SWL) `.rdbdata` archive files. It needs a real SWL install directory to do anything; there is no sample data and no test project in the repo.

## Commands

```sh
dotnet build RdbExporter.sln
dotnet run --project RdbExporter -- -i "<SWL install dir>" -l          # list known types used by SWL (-la for all)
dotnet run --project RdbExporter -- -i "<SWL install dir>" --rdb strings # export by name or numeric ID
dotnet run --project RdbExporter -- -i "<SWL install dir>" --rdb 1010042 -r  # force raw .dat export
dotnet run --project RdbExporter -- -i "<SWL install dir>" -di         # dump le.idx to Index.json
```

Exports are written to `<current dir>/<TypeId> - <Name>/`.

## Architecture

**Index → entry → exporter pipeline.**
1. `Parsers/IBDRParser` reads `<install>/RDB/le.idx` (magic `IBDR`) into a list of `IDBRIndexEntrty` (RDB type, ID, file number, offset, length). Entries with file number 255 are dropped. `Helpers.GetRdbIndex` caches this statically.
2. `IDBRIndexEntrty.OpenEntryFile` opens `RDB/NN.rdbdata`, seeks to `FileOffset - 16`, validates a 16-byte header (type, id, length, unknown), and then detects an optional extra 12-byte header that starts with the type ID. If present, it skips it and **mutates `FileLength`** to the payload length. Consumers should read `entry.FileLength` bytes only *after* calling `OpenEntryFile`.
3. `Program.cs` resolves the `--rdb` argument against `Data/RDBTypes.json`, filters index entries to that type, builds `ExportParameters`, and instantiates the exporter **by reflection** from the JSON `exporterType` string (`Type.GetType(...)` + `Activator.CreateInstance`). Unknown numeric types and `-r` fall back to `RawFileExporter` with `.dat`.

**Adding support for an RDB type** is usually just a `Data/RDBTypes.json` entry (`name`, `id`, `isSwl`, `exporterType` as a fully qualified class name, `exporterArguments`, `otherDesc`). JSON is case-insensitive and allows trailing commas. A new format needs a class implementing `Exporters/IExporter` (`RunExport(ExportParameters)`). Exporters typically use `Parallel.ForEach` over `RdbFileEntries`.

**Exporters of note:**
- `RawFileExporter` takes the file extension as `Arguments[0]`. It uses the filename index (RDB type `1000010`, ID 1, parsed by `FilenameIndexParser` and cached in `Helpers.GetFilenameIndex`) for real filenames when available.
- `StringExporter` ignores `RdbFileEntries` and pulls type `1030002` (TDC2 string tables, `TDC2Parser`) directly from the index. It names output via language files `<install>/Data/Text/*.tdbl` (TDL1, `TDL1Parser`), and writes per-language JSON plus an `Unknown` folder for unreferenced tables.
- `SwfImageExporter` / `SwfImageParser` extract bitmaps from embedded SWF, using `Utilities/BittableBinaryReader` for bit-level reads.
- `MergedJpgExporter` splits concatenated JPEGs on `FF D9` and stitches map tiles using hard-coded grid-size detection, with a 7px overlap offset.
- `VoiceoverExporter` slices from the first `OggS` marker.

## Platform caveats

The code was written for Windows:
- `Helpers` loads `Data/RdbTypes.json`, but the file on disk is `Data/RDBTypes.json`. On a case-sensitive filesystem this lookup fails.
- `StringExporter` builds a path with a literal backslash (`@"Data\Text\"`).
- Image exporters depend on `System.Drawing.Common` (GDI+).

`Data/StringTypes.json` is copied to the output directory but is not referenced by any code.
