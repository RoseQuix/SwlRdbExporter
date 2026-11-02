# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 8 console tool (`RdbExporter`) that extracts assets from Secret World Legends (SWL) `.rdbdata` archive files. It needs a real SWL install directory to do anything; there is no sample data and no test project in the repo.

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
- `RawFileExporter` takes the file extension as `Arguments[0]`. Output names come from `Helpers.GetOutputPath`, which uses the filename index (RDB type `1000010`, ID 1, parsed by `FilenameIndexParser` and cached in `Helpers.GetFilenameIndex`) for real filenames when available, and appends `.<id>` to names that several IDs of a type share.
- `FctxExporter` / `FctxParser` convert FCTX textures to DDS (`Utilities/DdsWriter`), one file per layer (a MIXD normal map's ATI1 layer gets `_gloss`). FCTX mips are stored smallest first and get reversed; MIXD is BC5 with an unused 6-byte block before each mip's blocks, written as `ATI2`. Non-FCTX entries in 1010004 (TGA, PNG, DDS) are written as they are.
- `StringExporter` ignores `RdbFileEntries` and pulls type `1030002` (TDC2 string tables, `TDC2Parser`) directly from the index. It names output via language files `<install>/Data/Text/*.tdbl` (folder name matched case-insensitively) (TDL1, `TDL1Parser`), and writes per-language JSON plus an `Unknown` folder for unreferenced tables.
- `BinaryXmlExporter` / `LmxbParser` convert LMXB binary XML (1010500–1010516) to text XML. Each type's bundle (ID 1) has many roots, which get wrapped in `<bundle>`. Characters XML can't hold become U+FFFD.
- `ProtobufExporter` decodes protobuf types to JSON. `Arguments[0]` names the message; the classes are generated at build time (Grpc.Tools) from `Protos/*.proto`, schemas recovered from the client exe, with `option csharp_namespace = "RdbExporter.Protos"` added. `LooksPacket.proto`'s `HasAttractorPlacement` is renamed (keeping its JSON name) because it clashes with the generated presence property of `AttractorPlacement`.
- `LuaScriptExporter` splits the Lua script list (1010223) into `ScriptID<id>.lua` files (the module name scripts `require`) plus `Index.json` with each script's source.
- `MeshExporter` / `FmeParser` convert FME meshes (1010001) to OBJ (positions, normals, first UV set, one group per submesh) with an `.mtl` whose `map_Kd` / `map_Bump` point at `../1010004 - Textures/`, where the texture export writes the DDS files. Material parameters such as `g_DiffuseTex=0` index the submesh's texture list.
- `JsonExporter` is the base for binary → JSON exporters: override `Parse(parameters, entry, data)` and throw `InvalidDataException` for entries that don't parse.
- `ItemTemplateExporter` / `ItemTemplateParser` decode item (1000620) and spell (1000621) templates. Some entries store string lengths in UTF-16 units (a bug in Funcom's export tool); the parser backtracks over the possible readings and records them in `StringReadings`.
- `DataTableExporter` / `DataTableParser` decode the 42 small one-entry tables (feats, hair, playfield parents, XP, tokens, game tweaks, login rewards, …), one layout function per type. Columns without a known meaning are written as `Values` arrays.
- `HeightmapExporter` writes GHM1 heightmaps (1066604) as 16-bit greyscale PNG through `Utilities/PngWriter` (SkiaSharp can't encode 16-bit grey), with a JSON sidecar holding the scale.
- `RasterExporter` writes the {u32 version, u32 width, u32 height} + texels rasters: `R8` (FAOGen AO textures, 1010043) as 8-bit PNG, `R32F` (rain/snow occluder heights, 1066611) as PFM.
- `StatelsExporter` / `StatelsParser` decode the per-playfield static mesh placements (1000060): visual type and id (named from the filename index), position, rotation quaternion (x, y, z, w), scale and flags. Version 0x5000 stores matrices, which are also converted.
- `LayoutExporter` decodes binary types with a layout function and writes JSON; `Arguments[0]` names the layout in `PlacementParser.Layouts` (per-playfield types) or `AssetParser.Layouts` (other small formats); with `Arguments[1]` = `KeepInnerHeader` the layout gets the entry with its inner header (`IDBRIndexEntrty.ReadAllBytes(dir, true)`). Layouts build dictionaries (field names from the notes), read through `Utilities/SpanReader` (throws `InvalidDataException` past the end) and must consume the whole entry.
- `CollisionMeshExporter` writes per-object collision meshes (1010060, and the unused older 1010034) as OBJ in local space, one group per sub-mesh.
- `CollisionTagMapExporter` writes collision tag maps (1066609; raw after `CTM1`, zlib after an inner header) as an 8-bit PNG of per-metre terrain layer slots plus JSON with the 256 footstep / occlusion tags.
- `LayerHeightmapExporter` writes terrain layer height maps (1000069) as an 8-bit PNG of the top mip plus the baked BC5 normal map as `<id>_normal.dds`.
- `Cat2Exporter` / `Cat2Parser` write CAT2 skeletons (1010002) and animations (1010003) as JSON. Version 3 tracks only store an FNV-1 hash of the bone name, so names are resolved from every skeleton's bones. Animation JSON isn't indented (`JsonExporter.GetOptions`).
- `WaterExporter` splits water data (1000054) into 16-bit PNGs of the four u16 cell fields, an RGB PFM of the foam coordinates, the portal mesh as OBJ and a JSON header.
- `SwfImageExporter` / `SwfImageParser` extract bitmaps from embedded SWF, using `Utilities/BittableBinaryReader` for bit-level reads.
- `MergedJpgExporter` stitches the largest level of each map (1010013) using the MTIL tile index with the same ID (1010012, `MtilParser`), cropping each tile around its centre to the level's tile size (most maps' tiles have a 4-pixel border).
- `VoiceoverExporter` slices from the first `OggS` marker.

## Cross-platform notes

The tool runs on Linux and Windows. Keep it that way:
- Image code uses SkiaSharp, with Linux native assets from NuGet (`SkiaSharp.NativeAssets.Linux.NoDependencies`). Don't reintroduce `System.Drawing`, which is Windows-only on modern .NET.
- Build paths with `Path.Combine`, and match the casing of files in the repo exactly. Game install folders can differ in case between platforms (Linux installs have `Data/text`), so `StringExporter` finds that folder case-insensitively.
- `SwfImageParser.FixJpegTables` merges SWF JPEG tags whose encoding tables sit in a separate SOI..EOI block. libjpeg rejects that layout, though GDI+ tolerated it.

`Data/StringTypes.json` is copied to the output directory but is not referenced by any code.
