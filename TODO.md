# PCGUtil — TODO

Task tracker for the PCGUtil PCG inspector. Dates are `YYYY-MM-DD`.
A task is **open** until it has a Completed date; then move it to the Done table.

`[T1]`–`[T5]` are effort/scope tiers, not a strict order: **T1** rides existing, verified
machinery; **T2** adds decoding or reports (no new write machinery); **T3** needs a new
architectural concept (multi-file model, rule engine); **T4** opens a new editing domain
(parameters, bulk edit, sysex); **T5** is the strategic long-pole (multi-model coverage).
*(parity)* marks gaps versus PCG Tools, the current benchmark.

## Open

| Task | Created | Completed |
|------|---------|-----------|
| Decode song timbres so program/combi reorg retargets song references too (set-list Song slots already decoded) | 2026-06-02 | — |
| Add a hex viewer for a selected chunk | 2026-06-02 | — |
| Editing polish: clear/init a slot or combi; drag-to-position reorder | 2026-06-02 | — |
| [T4] Timbre reordering within a combi (rides on the combi-timbre decode) | 2026-06-04 | — |
| [T4] Parameter editing beyond names (program/combi params; global master tuning) | 2026-06-04 | — |
| [T4] Sysex .syx/.mid export ("send to hardware") | 2026-06-04 | — |
| [T2] More reports: whole-file content list, DAW patch-list export *(parity; combi content list done 2026-07-04)* | 2026-07-02 | — |
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
| Move/reorganize Programs — SwapPrograms with dual retargeting (combi timbres + program set-list slots); Programs tab; integrity test (hardware confirmed 2026-07-03) | 2026-06-05 | 2026-06-05 |
| Bank labels (INT-A / USER-A / USER-AA) shown in Programs / Combis / Usage / Set Lists tabs | 2026-06-05 | 2026-06-05 |
| Duplicate detection — programs/combis grouped by name with byte-identical flag; Duplicates tab | 2026-06-05 | 2026-06-05 |
| Name search — case-insensitive search over program / combi / set-list names; Search tab | 2026-06-05 | 2026-06-05 |
| HTML report export — printable set-list sheets (current / all) + usage report; Set Lists and Usage tabs | 2026-06-07 | 2026-06-07 |
| Fix per-chunk checksum on edit — recompute each leaf chunk's 8-bit data checksum (root cause of hardware "File unavailable"); PcgChecksum + PcgEditor | 2026-06-07 | 2026-06-07 |
| Find/sort/filter navigation aids — Find box on Programs/Combis tabs; Find + Bank + Sort on Usage tab | 2026-07-02 | 2026-07-02 |
| Ref-safe sort + compact banks — whole-bank permutation reorder (ReorderPrograms/ReorderCombis + PcgOrganizer), init/empty placeholders to the tail, dual retarget; Sort A–Z / Compact buttons (hardware confirmed 2026-07-03) | 2026-06-04 | 2026-07-02 |
| Publish to hildner.org/pcgutil — self-contained win-x86 (32-bit shared pool), PathBase-aware base href, scripted FTPS deploy (deploy/deploy-ftp.ps1); WebSocket transport verified live | 2026-07-02 | 2026-07-02 |
| Landing-page card + app polish — PCG Util card on hildner.org index, template About link removed, hardware checklist exposed in sidebar (now wwwroot/hardware-testing.html) | 2026-07-02 | 2026-07-02 |
| User-facing test page — served checklist rewritten for visitors (no dev statuses, file-agnostic wording, GitHub-issues report link); HARDWARE-TESTING.md stays the internal tracker | 2026-07-03 | 2026-07-03 |
| Cross-file copy + clone (T3 multi-file model) — Copy tab with read-only source file, CopyProgram/Combi/SetListSlotAcross + PcgCompat same-model gate, combi timbre preview, download-a-copy buttons; checklist section 7 (hardware confirmed 2026-07-04) | 2026-06-04 | 2026-07-03 |
| Differences report (T3) — PcgDiff byte-level compare of two open files (moved/renamed/edited/added/removed/replaced, reorg-aware move pairing, retarget details on slots); Differences tab + CSV | 2026-07-02 | 2026-07-04 |
| Slot notes + re-point (T2 parity + polish) — description field decoded at slot+30 (512 ASCII chars, line breaks), Notes editor, notes on printed set lists + CSV; Load button re-points a slot's patch (name/notes preserved); checklist section 8 (hardware confirmed 2026-07-04) | 2026-07-02 | 2026-07-04 |
| Musician-first navigation — technical tabs (File info/Strings/Chunk tree) behind an "Advanced" expander; upload lands on Set Lists | 2026-07-04 | 2026-07-04 |
| Combi timbre inspector + contents report — Timbres expander on Combis tab (per-timbre status + resolved program); printable bank contents sheet + CSV (T2 parity: combi content list) | 2026-07-04 | 2026-07-04 |
| Musician-first UX step 2: "Add sounds from another file" wizard — guided four-step flow (choose file / pick the song with timbre previews / sentence-form review with auto-picked free slots, per-type program banks, KGE note, adjust disclosure / done with download + follow-ups incl. the Contents=All reminder); reachable from the Start card, Copy tab unchanged; FreeCombiSlots helper | 2026-07-15 | 2026-07-15 |
| Musician-first UX step 1: Start page — post-upload landing asks "What would you like to do?" with six task cards in plain language (Prepare a gig / Add sounds from another file / Explore / Tidy up / Compare backups / Under the hood), leftmost Start tab, unsaved-edits badge + download on the landing, reworded intro; steps 2-5 (wizard, theme, language pass, touch) tracked in conversation | 2026-07-15 | 2026-07-15 |
| Program bank engine types (HD-1/EXi) — decoded from the bank chunk id (PBK1/MBK1), shown on every program-bank picker, and enforced on every program move (deep copy splits per type into two destination banks; shallow copies, paste, and swaps refuse mismatches) — a type-mismatched bank made the instrument refuse the whole file | 2026-07-15 | 2026-07-15 |
| Deep copy, beyond-parity — "Combi + its programs" on the Copy tab: PcgDeepCopy plans the transplant (dependency classification, byte-identical program reuse, free-slot allocation, KARMA user-GE warning) and CopyCombiDeepAcross lands programs + combi and retargets the copied timbres in one checksum pass; checklist section 9 (hardware pending) | 2026-07-14 | 2026-07-14 |
| Drum Kit & Wave Sequence names — decoded into the catalog (same bank layout), searchable, browse lists on File info | 2026-06-02 | 2026-07-04 |
