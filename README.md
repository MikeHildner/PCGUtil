# PCGUtil

Inspect and reorganize your **`.PCG`** workstation backup files right in your browser —
rename and reorder set lists, combis, and programs, find duplicates, and see what uses what,
then download a new `.PCG` to load back onto your instrument.

Your uploaded file is **never modified**. Every edit is made on a copy held in memory, and
you save the result as a brand-new `.PCG` file.

## What you can do

Main tabs:

- **Set Lists** — see every set list and the program or combi each slot loads. In **Edit
  mode** you can rename, reorder, and copy slots; a moved slot keeps pointing at the same sound.
- **Combis** — browse a bank and reorganize: reorder, copy, or rename combis. Reordering
  automatically updates the set-list slots that use them, so your songs keep their sound.
  Each combi's key and velocity zones are editable right in its zone map, and you can change
  which program a layer plays — picked by name — or switch a layer off entirely. A layer's
  program can bring its **effects** along too: they pack into the combi's free effect slots,
  leaving everything already there untouched, so the layer sounds like the program did on
  its own. Every effect chip is clickable — it opens that effect's **actual settings**
  (wet/dry, EQ gains, delay times) straight from the file, no instrument required. Layers
  that tweak their program show a **✎** count: click it to see exactly what the combi
  changed, by name — an organ layer reads out as its drawbar registration.
- **Programs** — the same for programs: reorder, copy, or rename. Every combi timbre and
  set-list slot that referenced a program is updated to follow it to its new spot. **Re-point
  references** retargets in bulk by rule — everything that plays one program (or a whole
  range, or a whole bank) plays another instead, with a preview of exactly what changes.
- **Songs** — open the `.SNG` saved beside your backup and see what each sequencer track
  loads. Move programs around and the tracks follow their sounds automatically; download the
  edited `.SNG` alongside the edited `.PCG`.
- **Merge** — your file in the middle, up to two other backups on the wings. Drag a program,
  combi, or song across and combis bring the programs they need; anything the target already
  has is reused instead of duplicated.
- **Search** — find any program, combi, or set-list slot by name across the whole file.

Behind **More**:

- **Differences** — every program, combi, and slot that changed between an older backup and
  the file you're editing.
- **Usage** — which programs are actually used and by what, plus a list of programs and combis
  that nothing references (cleanup candidates).
- **Duplicates** — programs and combis grouped by their sound data, so renamed copies still
  group together; names shared by different sounds are listed separately. In Edit mode, one
  click re-points every reference at a single copy, so the twins stop being used and can be
  cleared.
- **Copy** — precise slot-level copies between two files, including set-list slots.
- **File info**, **Strings**, **Chunk tree** — file structure and raw data.

Export lists to **CSV**, and download your edited **`.PCG`**.

## How to use it

1. **Back up your instrument first.** Always keep your original `.PCG` safe.
2. Open PCGUtil and **upload a `.PCG`** file. It is read into memory and is not saved to disk
   by the app.
3. Browse the tabs. Flip the **Browse | Edit** switch to make changes — every edit can be
   undone (Ctrl+Z / the ↶ button) and redone, and edits accumulate in memory as unsaved
   until you save. To pull sounds in from other backups, use the **Merge** tab — or the
   guided "Add one song, step by step" flow if you'd rather take it one song at a time.
4. Click **Download edited .PCG** to save a new file. Your uploaded file is left untouched.
5. Load the new `.PCG` onto your instrument and **double-check it sounds right** before a gig.

## Good to know

- **Reorganizing is reference-safe.** When you reorder a combi or program, PCGUtil rewrites
  the things that point at it — set-list slots, combi timbres, and song tracks — so nothing
  ends up loading the wrong sound.
- **Test edits on your hardware before relying on them**, especially after reorganizing
  programs: load the edited file and confirm a few set-list songs and combis recall the right
  sounds.
- Works with `.PCG` backup files from supported workstations, plus the companion `.SNG` and
  `.KGE` files saved beside them.

## Running it

PCGUtil is a .NET 10 Blazor web app. From the project folder:

```
dotnet run --project src/PcgUtil.Web
```

Then open the address it prints (for example `http://localhost:5229`) and upload a `.PCG`.

## License

MIT — see [LICENSE](LICENSE). © 2026 Mike Hildner.
