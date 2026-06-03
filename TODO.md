# PCGUtil — TODO

Task tracker for the PCGUtil PCG inspector. Dates are `YYYY-MM-DD`.
A task is **open** until it has a Completed date; then move it to the Done table.

## Open

| Task | Created | Completed |
|------|---------|-----------|
| Resolve Set List slot references (raw bytes at slot +8) to Program/Combi/Song names | 2026-06-02 | — |
| Decode program / combi / drum-kit bank records (name + bank/index) | 2026-06-02 | — |
| Add a hex viewer for a selected chunk | 2026-06-02 | — |

## Done

| Task | Created | Completed |
|------|---------|-----------|
| Identify the file format and inspect the sample (`files/20260602.PCG`) | 2026-06-02 | 2026-06-02 |
| Scaffold Blazor Server (.NET 10) + `PcgUtil.Core` library solution | 2026-06-02 | 2026-06-02 |
| Implement chunk-tree parser (`PcgReader`) and printable-string extraction | 2026-06-02 | 2026-06-02 |
| Build inspector UI: upload, Overview / Set Lists / Strings / Chunk-tree tabs, CSV/JSON export | 2026-06-02 | 2026-06-02 |
| Add xUnit tests against the sample file | 2026-06-02 | 2026-06-02 |
| Decode Set List set-list + slot names (SBK1 → SetListReader, with tests) | 2026-06-02 | 2026-06-02 |
