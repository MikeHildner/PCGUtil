# Hardware testing checklist

PCGUtil's reference retargeting is covered by automated tests, so these checks confirm two
things on the workstation itself:

1. it **accepts** a surgically-edited `.PCG` without error, and
2. its interpretation of the references **matches ours** — every patch still recalls the same
   sound after an edit.

This file is the **internal tracker**: per-section Status lines record what has been verified
on our own hardware, and items may reference patches from our sample file. The public,
file-agnostic variant every visitor sees is `src/PcgUtil.Web/wwwroot/hardware-testing.html`
(linked from the app top bar as "Hardware tests") — same checks in substance, no statuses,
worded for any instrument, with a GitHub-issues link for reports. **When a write-path feature
ships, add a section to both.** Tip: use the **Usage** and **Duplicates** tabs to choose which
patches to test with.

## 0. Setup & safety (do first)
- [ ] Back up the workstation's current state to a fresh `.PCG`.
- [ ] Prefer testing on a USER bank (not irreplaceable data); keep the backup to restore.
- [ ] Note a baseline: pick 3–4 set-list songs and remember what each sounds like / loads.

## 1. File loads (smoke test)
Status: **confirmed** on hardware (2026-06-07).
- [ ] Make any small edit, download, and load the edited `.PCG` — it loads with no file error or hang.

## 2. Set-list editing
Status: **confirmed** on hardware (2026-06-07; rename/copy needed the per-chunk checksum fix, now verified).
- [ ] Reorder slots: the order changes and each song recalls the same sound.
- [ ] Rename a slot and the set list: the new names show on the device.
- [ ] Copy a slot: the destination recalls the source's sound.

## 3. Combi reorganization
Status: **confirmed** on hardware (2026-06-07).
- [ ] Reorder two combis in a USER bank (e.g. "Let's Go Crazy", USER-A #057): they swap positions and each sounds the same.
- [ ] A set-list song that used a moved combi still recalls the correct combi (the reference followed the swap).
- [ ] Copy a combi: the destination sounds like the source. Rename: the name shows.

## 4. Program reorganization
Status: **confirmed** on hardware (2026-07-03; dual retarget — combi timbres + program-type slots).
- [ ] In the **Usage** tab (read-only), set **Sort** to *Most used* and **Bank** to a USER bank — the top row is the best candidate. Note its bank + number and one combi from its "show" list.
- [ ] In the **Programs** tab, choose that bank and type the number (or name) in **Find** to jump to the program. Turn on **Edit mode**, clear Find so the neighbor is visible, and click ▼ (or ▲) to swap — references retarget automatically.
- [ ] The programs are in their new positions and each sounds right.
- [ ] Combis that used the moved program still load it in every timbre — open a couple from its "Used by" list; they sound identical to before.
- [ ] A set-list slot that loads the moved program directly still recalls it.
- [ ] Copy a program: the destination sounds like the source. Rename: the name shows.

## 5. Song-slot guard (edge case)
Status: **confirmed** on hardware (2026-07-03).
- [ ] The Song-type slot "Sequence" (Set List 15, slot 31) is unchanged after a combi/program reorg — especially one that touches the first INT-A combi/program (bank 0, #0), whose bytes it collides with.

## 6. Sort & compact banks
Status: **confirmed** on hardware (2026-07-03).
- [ ] Sort a USER combi bank A–Z (Combis tab → Edit mode → **Sort A–Z**): the device shows the new order, init/empty slots last, and a set-list song that used a sorted combi still recalls the same sound.
- [ ] Sort or compact a USER program bank: a combi that used a moved program still sounds identical, and a program-type set-list slot still recalls its program.
- [ ] **Compact** only moves init/empty slots to the end — every named patch keeps its relative order.

## 7. Cross-file copy
Status: **confirmed** on hardware (2026-07-04).
- [ ] Open a second backup as the source (Copy tab) and copy a program into a USER slot: the edited file loads and the copied program sounds like it did in the source file.
- [ ] Copy a combi across: the destination recalls it, and its timbres play the destination's programs at those slots (compare with the Copy tab's timbre preview before downloading).
- [ ] Copy a set-list slot across: the slot recalls whatever its reference points at in the destination file.

## 8. Slot notes & re-point
Status: **confirmed** on hardware (2026-07-04).
- [ ] Set a slot's notes (Set Lists tab → Edit mode → **Notes**): the comment shows on the device's Set List display.
- [ ] Re-point a slot (**Load** button) at a different combi, and at a program: the slot recalls the new patch; its name and notes stay put.

## 9. Deep copy (combi + its programs)
Status: **pending**.
- [ ] Deep-copy a combi from a second file ("Combi + its programs" on the Copy tab): the destination recalls it and it sounds like it did in the source file (compare a shallow "Combi only" copy of the same combi to hear the difference).
- [ ] The copied programs landed only in the chosen bank's free (Init/empty) slots — no named program was overwritten.
- [ ] Each program landed in a bank of its own engine type (the app offers matching HD-1/EXi banks only) and the file loads without "File unavailable".
- [ ] Deep-copy a second combi from the same source: shared programs are reused (plan preview says "reuses"), not copied twice.
- [ ] If the plan warned about user KARMA GEs, load the source's matching .KGE; KARMA then plays as in the source.

## 10. Effects & KARMA readout (read-only decode)
Status: **confirmed** on hardware (2026-07-17; all four checks matched exactly).
No write path — this section verifies that the decoded effect/KARMA labels match the
instrument's own screens (an off-by-one in the name table would shift every label).
- [x] INT-A 000 "K-Lab: Katja's House": IFX1–7 = Stereo Chorus / St. BPM Auto Panning Dly /
      Stereo Limiter / Stereo BPM Delay / Stereo Limiter / Stereo Graphic 7EQ / Stereo Dyna
      Compressor; MFX1 Stereo BPM Delay, MFX2 Reverb Hall, TFX1 Stereo Master 3EQ,
      TFX2 Stereo Mastering Limiter.
- [x] INT-A 023 "Metal Morphosis": IFX1 = Stereo Auto Fade Mod. (neighbors in the effect list
      are Stereo Vibrato / 2-Voice Resonator, so ±1 misalignment would be unmistakable).
- [x] INT-A 009 "Smooth Jazzmitazz": reverb on MFX1 (Overb), MFX2 empty — MFX slot order.
- [x] User combi "TOM SAWYER": IFX1 L/C/R Delay loaded but switched **off**, IFX4 St. Tube
      PreAmp Modeling — the on/off bit verified on user content.

## 11. Slot colors, volume & transpose
Status: **confirmed** on hardware (2026-07-18 — decode via probe file: set list 016 colored
0–15 in picker order, volume 100, transpose +2/−1 all read back exactly, gig-list readings
0/−1/−2 matched the ×32 encoding; color WRITE round-trip also confirmed same day: a slot
recolored in PCGUtil showed the chosen color on the instrument's set list).
- [x] Decode: slot colors/volume/transpose match the instrument (probe file + gig-list readings).
- [x] Recolor a slot in PCGUtil (Set Lists → Edit mode → **Settings**), download, load on the
      instrument: the set list shows the chosen color and the slot still recalls its patch,
      name, and notes.
- [x] Set a slot's **volume / transpose / hold time** in the same Settings panel, download,
      load: the instrument shows the transpose in the slot, plays at the set volume, and
      holds for the chosen time when switching away (confirmed on hardware 2026-07-18 —
      the full slot Settings write path is verified end to end).

## 12. Program categories & EXi engines (read-only decode)
Status: **confirmed** (2026-07-18 — category + sub-category verified against the published
voice name list at 768/768 factory programs, EXi engine at 640/640; the favorite bit was
located by the star-one-program experiment: diffing two hardware exports around starring
USER-GG 000 exposed a single-byte flip at record offset 2558 bit 5 — the initial
combi-idiom guess of 2569 bit 0 was wrong and has been corrected; the starred combi
USER-A 096 "JUMP" also confirmed the combi favorite bit at 4791 bit 0 by the same diff).
- [x] Category/engine spot check: INT-A 000 Berlin Grand = Keyboard · SGX-2, INT-A 040 =
      Organ · CX-3, INT-C 059 Harpsichord = Keyboard · STR-1, INT-B 000 = Brass (HD-1).
- [x] Star one program on the instrument, save a PCG, and confirm PCGUtil shows ★ on
      exactly that program (GET LUCKY VOCODER — confirmed in the UI after the fix).

## 13. Content-hash init detection (read-only decode)
Status: **pending** — software-verified 2026-07-21 (sound-hash grouping + renamed-init
detection, 191-test suite); the hardware side is one recall.
- [ ] Recall USER-A #026 "Band On The Run" (or "Lust Girl" USER-A #064, "WHAT I LIKE ABOUT
      YOU" USER-C #020): despite the song name, it plays a bare init combi — exactly what
      the Duplicates tab's "init placeholders with a real name" list claims.

## 14. Row actions: drag-to-position & clear
Status: **pending** — write paths are permutations/copies of §2–§6's hardware-confirmed
primitives (the slot reorder is byte-identical to chains of the §2 swap, pinned by test),
so this is a load formality.
- [ ] Drag a set-list slot by its ⠿ grip to a new position, download, load: the song sits
      where it was dropped, the slots between shifted one step, and every song still recalls
      its sound, name, notes, and color.
- [ ] Drag a combi (and a program) to a new position: it lands there and the set lists /
      combis that use the moved patches still recall the same sounds.
- [ ] Right-click a slot → **Clear slot**, download, load: the slot shows as empty on the
      instrument's set list.
- [ ] Right-click a combi or program → **Clear to init**, download, load: the slot plays a
      bare init patch and the file loads with no error.

## 15. Timbre quick-edit (key/velocity zones, volume, transpose)
Status: **pending — REQUIRED before this feature counts as verified.** These are the first
writes ever made to timbre bytes +37/+38 (key zone), +40/+41 (velocity zone), +5 (volume),
and +7 (transpose); the offsets were decoded from a vendor pack's prose set-list notes and
have never been confirmed by a hardware write round-trip.
- [ ] In a USER combi (Combis tab → Edit → **Timbres**), set one timbre's key zone to a
      distinctive split (e.g. C4–G9), download, load: the instrument's combi Timbre
      Parameters page shows exactly that bottom/top key, and playing across the split point
      confirms it.
- [ ] Set a velocity zone on a timbre (e.g. 89–127), download, load: soft notes skip the
      timbre, hard notes trigger it, and the instrument shows the same velocity window.
- [ ] Set a timbre's volume and transpose, download, load: the mix balance changes and the
      timbre sounds transposed by the chosen semitones; the instrument's mixer page shows
      the written values.
- [ ] The combi's other timbres — programs, zones, status — are untouched.

## Known limitation
- Sequencer **songs** that reference a moved program are **not** retargeted (set-list and combi
  references are). If you use songs, spot-check a song's tracks after a program reorg.

## Pass criteria
The edited `.PCG` loads cleanly, and **every patch and reference recalls the same sound as
before** — only the positions you intentionally changed should differ.
