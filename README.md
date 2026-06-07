# PCGUtil

Inspect and reorganize your **`.PCG`** workstation backup files right in your browser —
rename and reorder set lists, combis, and programs, find duplicates, and see what uses what,
then download a new `.PCG` to load back onto your instrument.

Your uploaded file is **never modified**. Every edit is made on a copy held in memory, and
you save the result as a brand-new `.PCG` file.

## What you can do

- **Overview** — a quick summary of the file and the sections it contains.
- **Set Lists** — see every set list and the program or combi each slot loads. In **Edit
  mode** you can rename, reorder, and copy slots; a moved slot keeps pointing at the same sound.
- **Combis** — browse a bank and reorganize: reorder, copy, or rename combis. Reordering
  automatically updates the set-list slots that use them, so your songs keep their sound.
- **Programs** — the same for programs: reorder, copy, or rename. Every combi timbre and
  set-list slot that referenced a program is updated to follow it to its new spot.
- **Usage** — which programs are actually used and by what, plus a list of programs and combis
  that nothing references (cleanup candidates).
- **Duplicates** — programs or combis that share a name, flagged when they are byte-for-byte
  identical (a true redundant copy, versus the same name reused for a different sound).
- **Search** — find any program, combi, or set-list slot by name across the whole file.
- Export lists to **CSV**, and download your edited **`.PCG`**.

## How to use it

1. **Back up your instrument first.** Always keep your original `.PCG` safe.
2. Open PCGUtil and **upload a `.PCG`** file. It is read into memory and is not saved to disk
   by the app.
3. Browse the tabs. Turn on **Edit mode** where available to make changes; edits accumulate in
   memory and are shown as unsaved until you save.
4. Click **Download edited .PCG** to save a new file. Your uploaded file is left untouched.
5. Load the new `.PCG` onto your instrument and **double-check it sounds right** before a gig.

## Good to know

- **Reorganizing is reference-safe.** When you reorder a combi or program, PCGUtil rewrites
  the things that point at it — set-list slots and combi timbres — so nothing ends up loading
  the wrong sound.
- **Test edits on your hardware before relying on them**, especially after reorganizing
  programs: load the edited file and confirm a few set-list songs and combis recall the right
  sounds.
- **Sequencer songs aren't fully covered yet.** If a song references a program you move, that
  reference isn't updated automatically (set-list and combi references are).
- Works with `.PCG` backup files from supported workstations.

## Running it

PCGUtil is a .NET 10 Blazor web app. From the project folder:

```
dotnet run --project src/PcgUtil.Web
```

Then open the address it prints (for example `http://localhost:5229`) and upload a `.PCG`.

## License

MIT — see [LICENSE](LICENSE). © 2026 Mike Hildner.
