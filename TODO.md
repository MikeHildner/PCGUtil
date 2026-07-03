# PCGUtil — TODO

Task tracker for the PCGUtil PCG inspector. Dates are `YYYY-MM-DD`.
A task is **open** until it has a Completed date; then move it to the Done table.

## Open

| Task | Created | Completed |
|------|---------|-----------|
| Decode song timbres so program/combi reorg retargets song references too (set-list Song slots already decoded) | 2026-06-02 | — |
| Decode Drum Kit & Wave Sequence bank names (program/combi names done) | 2026-06-02 | — |
| Add a hex viewer for a selected chunk | 2026-06-02 | — |
| Editing polish: clear/init a slot or combi; drag-to-position reorder; re-point a slot's patch | 2026-06-02 | — |
| [T3] Open two files + copy programs/combis/slots between same-model files (multi-file model) | 2026-06-04 | — |
| [T3] Clone an entire PCG (save-as) | 2026-06-04 | — |
| [T4] Timbre reordering within a combi (rides on the combi-timbre decode) | 2026-06-04 | — |
| [T4] Parameter editing beyond names (program/combi params; global master tuning) | 2026-06-04 | — |
| [T4] Sysex .syx/.mid export ("send to hardware") | 2026-06-04 | — |
| [T2] Set-list slot description field — decode + view/edit (multi-line comment) *(parity)* | 2026-07-02 | — |
| [T2] More reports: combi content list, whole-file content list, DAW patch-list export *(parity)* | 2026-07-02 | — |
| [T3] Differences report — compare two PCGs, list changed/moved patches *(parity; pairs with multi-file)* | 2026-07-02 | — |
| [T3] Rule-based reference changer — bulk retarget program refs by pattern/rule *(parity)* | 2026-07-02 | — |
| [T4] Bulk edit — select multiple combis/slots and edit shared fields at once *(parity)* | 2026-07-02 | — |
| [T5] Multi-model coverage — detect model family from header; per-family format tables (currently one family) *(parity long-pole)* | 2026-07-02 | — |

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
| Validate write path on hardware (surgical slot swap → the workstation accepts edited PCG) | 2026-06-02 | 2026-06-02 |
| Set list editing — reorder / rename / copy slots, surgical writes, download new .PCG | 2026-06-02 | 2026-06-02 |
| Study PCG Tools (LGPL C#) — confirmed reference model + timbre/slot ref byte fields | 2026-06-02 | 2026-06-02 |
| Combi reorganization — swap / copy / rename combis, retarget set-list refs, usage counts | 2026-06-02 | 2026-06-02 |
| Re-examine PCG Tools — confirmed combi-timbre layout (4802 / 16×188) + program-bank PcgId map; 99.7% resolve verified on sample | 2026-06-04 | 2026-06-04 |
| Format fixes + CombiReader: decode 3-way slot type (Song) + guard combi retargeting; PcgId program resolution; combi-timbre decode | 2026-06-04 | 2026-06-04 |
| Usage / cross-reference report — program usage sites + unreferenced programs/combis (init combis excluded); Usage tab + CSV | 2026-06-04 | 2026-06-04 |
| Move/reorganize Programs — SwapPrograms with dual retargeting (combi timbres + program set-list slots); Programs tab; integrity test (hardware re-confirm pending) | 2026-06-05 | 2026-06-05 |
| Bank labels (INT-A / USER-A / USER-AA) shown in Programs / Combis / Usage / Set Lists tabs | 2026-06-05 | 2026-06-05 |
| Duplicate detection — programs/combis grouped by name with byte-identical flag; Duplicates tab | 2026-06-05 | 2026-06-05 |
| Name search — case-insensitive search over program / combi / set-list names; Search tab | 2026-06-05 | 2026-06-05 |
| HTML report export — printable set-list sheets (current / all) + usage report; Set Lists and Usage tabs | 2026-06-07 | 2026-06-07 |
| Fix per-chunk checksum on edit — recompute each leaf chunk's 8-bit data checksum (root cause of hardware "File unavailable"); PcgChecksum + PcgEditor | 2026-06-07 | 2026-06-07 |
| Find/sort/filter navigation aids — Find box on Programs/Combis tabs; Find + Bank + Sort on Usage tab | 2026-07-02 | 2026-07-02 |
| Ref-safe sort + compact banks — whole-bank permutation reorder (ReorderPrograms/ReorderCombis + PcgOrganizer), init/empty placeholders to the tail, dual retarget; Sort A–Z / Compact buttons (hardware confirm pending) | 2026-06-04 | 2026-07-02 |
