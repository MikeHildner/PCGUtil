# PCGUtil — TODO

Task tracker for the PCGUtil PCG inspector. Dates are `YYYY-MM-DD`.
A task is **open** until it has a Completed date; then move it to the Done table.

## Open

| Task | Created | Completed |
|------|---------|-----------|
| Map bank indices to Kronos bank labels (INT-A/USER-A…); handle Song-type slot refs | 2026-06-02 | — |
| Decode Drum Kit & Wave Sequence bank names (program/combi names done) | 2026-06-02 | — |
| Add a hex viewer for a selected chunk | 2026-06-02 | — |
| Editing polish: clear/init a slot or combi; drag-to-position reorder; re-point a slot's patch | 2026-06-02 | — |
| Move/reorganize Programs (decode combi-timbre block; retarget timbres + set-list slots) | 2026-06-02 | — |

## Done

| Task | Created | Completed |
|------|---------|-----------|
| Identify the file format and inspect the sample (`files/20260602.PCG`) | 2026-06-02 | 2026-06-02 |
| Scaffold Blazor Server (.NET 10) + `PcgUtil.Core` library solution | 2026-06-02 | 2026-06-02 |
| Implement chunk-tree parser (`PcgReader`) and printable-string extraction | 2026-06-02 | 2026-06-02 |
| Build inspector UI: upload, Overview / Set Lists / Strings / Chunk-tree tabs, CSV/JSON export | 2026-06-02 | 2026-06-02 |
| Add xUnit tests against the sample file | 2026-06-02 | 2026-06-02 |
| Decode Set List set-list + slot names (SBK1 → SetListReader, with tests) | 2026-06-02 | 2026-06-02 |
| Resolve slot references to Program/Combi (type/bank/number) + name catalog (PcgCatalog) | 2026-06-02 | 2026-06-02 |
| Validate write path on hardware (surgical slot swap → Kronos accepts edited PCG) | 2026-06-02 | 2026-06-02 |
| Set list editing — reorder / rename / copy slots, surgical writes, download new .PCG | 2026-06-02 | 2026-06-02 |
| Study PCG Tools (LGPL C#) — confirmed reference model + timbre/slot ref byte fields | 2026-06-02 | 2026-06-02 |
| Combi reorganization — swap / copy / rename combis, retarget set-list refs, usage counts | 2026-06-02 | 2026-06-02 |
