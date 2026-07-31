# Hardware testing checklist

PCGUtil's reference retargeting is covered by automated tests, so these checks confirm two
things on the workstation itself:

1. it **accepts** a surgically-edited `.PCG` without error, and
2. its interpretation of the references **matches ours** — every patch still recalls the same
   sound after an edit.

**Status: every section is confirmed on hardware as of 2026-07-27** — §0–§20 are all green.
Every write path this app ships has been round-tripped through the instrument, from the first
set-list rename to timbre zones, drag reordering, deep merges, song retargeting,
program-effect copies, and rule-based re-pointing.

This file is the **internal tracker**: per-section Status lines record what has been verified
on our own hardware, and items may reference patches from our sample file. The public,
file-agnostic variant every visitor sees is `src/PcgUtil.Web/wwwroot/hardware-testing.html`
(linked from the app top bar as "Hardware tests") — same checks in substance, no statuses,
worded for any instrument, with a GitHub-issues link for reports.

**Keep the two in lockstep.** When a feature ships, add a section to *both* files **with the
same number and subject**, so "§16" means one thing in conversation. The numbering drifted
once (2026-07-26) because three read-only sections — Effects & KARMA, program categories, and
init detection — were only ever added here, leaving the public page three behind from §11 on.

Tip: use the **Usage** and **Duplicates** tabs to choose which patches to test with.

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
Status: **confirmed** on hardware (2026-07-25 — verified through the Merge view, which
runs the same planner and CopyCombiDeepAcross writer as the Copy tab's deep mode: the
merged combi sounded as it did in the source, copied programs landed only in free Init
slots, the file loaded clean, and a second combi from the same pack reused the shared
programs instead of copying them again).
- [x] Deep-copy a combi from a second file ("Combi + its programs" on the Copy tab, or a combi drag in the Merge view): the destination recalls it and it sounds like it did in the source file (compare a shallow "Combi only" copy of the same combi to hear the difference).
- [x] The copied programs landed only in the chosen bank's free (Init/empty) slots — no named program was overwritten.
- [x] Each program landed in a bank of its own engine type (the app offers matching HD-1/EXi banks only) and the file loads without "File unavailable".
- [x] Deep-copy a second combi from the same source: shared programs are reused (plan preview says "reuses"), not copied twice.
- [x] If the plan warned about user KARMA GEs, load the source's matching .KGE; KARMA then plays as in the source.

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
Status: **confirmed** on hardware (2026-07-24 — USER-A #026 "Band On The Run" recalled a
bare init combi on the instrument, exactly as the Duplicates tab claimed. Content-based
init detection is proven end to end: name-based matching would never have found it, and
the sound hash caught it in a real gig backup).
- [x] Recall USER-A #026 "Band On The Run" (or "Lust Girl" USER-A #064, "WHAT I LIKE ABOUT
      YOU" USER-C #020): despite the song name, it plays a bare init combi — exactly what
      the Duplicates tab's "init placeholders with a real name" list claims.

## 14. Row actions: drag-to-position & clear
Status: **confirmed** on hardware (2026-07-25 — all four checks passed in one file/one
load: insert-shift reordering of a set-list slot and of a combi both kept every reference
resolving, and both clear paths loaded cleanly). Write paths are permutations/copies of
§2–§6's confirmed primitives (the slot reorder is byte-identical to chains of the §2
swap, pinned by test), and the instrument agreed.
- [x] Drag a set-list slot by its ⠿ grip to a new position, download, load: the song sits
      where it was dropped, the slots between shifted one step, and every song still recalls
      its sound, name, notes, and color.
- [x] Drag a combi (and a program) to a new position: it lands there and the set lists /
      combis that use the moved patches still recall the same sounds.
- [x] Right-click a slot → **Clear slot**, download, load: the slot shows as empty on the
      instrument's set list.
- [x] Right-click a combi or program → **Clear to init**, download, load: the slot plays a
      bare init patch and the file loads with no error.

## 15. Timbre quick-edit (key/velocity zones, volume, transpose)
Status: **confirmed** on hardware (2026-07-22 — all three probe edits round-tripped: the
key-zone split, the velocity window, and the volume/transpose change each loaded and
behaved exactly as written. This was the first-ever write verification of timbre bytes
+37/+38 (key zone, top first), +40/+41 (velocity zone), +5 (volume), and +7 (transpose),
which until then rested on vendor-prose decode evidence only).
- [x] In a USER combi (Combis tab → Edit → **Timbres**), set one timbre's key zone to a
      distinctive split (e.g. C4–G9), download, load: the instrument's combi Timbre
      Parameters page shows exactly that bottom/top key, and playing across the split point
      confirms it.
- [x] Set a velocity zone on a timbre (e.g. 89–127), download, load: soft notes skip the
      timbre, hard notes trigger it, and the instrument shows the same velocity window.
- [x] Set a timbre's volume and transpose, download, load: the mix balance changes and the
      timbre sounds transposed by the chosen semitones; the instrument's mixer page shows
      the written values.
- [x] The combi's other timbres — programs, zones, status — are untouched.

## 16. Bend range & portamento readout (read-only decode)
Status: **confirmed** on hardware (2026-07-26 — the Bend and Porta columns match the
instrument's own pages, including both "follow the program" sentinels). Offsets +6/+36 were
originally derived from the prior-art model plus a value scan over ~13k live timbres (bend
holds musical semitone counts beside a −25 sentinel covering 84%; portamento pairs times
0–64 with a dominant 0xFF); the vendor SysEx dump then named both, and the instrument
agreed. Nothing writes these bytes, so this was always a display check, not a file risk.
**Where these live on the instrument** (Parameter Guide pp.470–471) — they are on two
*different* tabs, which is the easy thing to get wrong:
- **Bend Range** → COMBI → **P2: Timbre Parameters** → **2–3: Pitch**  `[PRG, −24…+24]`
- **Portamento** → COMBI → **P2: Timbre Parameters** → **2–2: OSC**  `[PRG, Off, 001…127]`

- [ ] **Bend:** open a combi with several different bend overrides and compare the app's Bend
      column against 2–3: Pitch, timbre by timbre. In the current backup, INT-A #000
      "K-Lab: Katja's House" is ideal — it should read Bend **0** on T1/T4, **+2** on
      T5–T11 and T13, and **+12** on T12.
- [ ] **Porta:** same idea against 2–2: OSC. INT-A #117 "Chance Encounter" should read
      Porta **34** on T2 and T4; INT-A #073 "Orange Ninja Split" **25** on T1; INT-A #040
      "Krunky Clav SW1" **Off** on T1/T2/T5/T7.
- [ ] "PRG" in either column corresponds to the instrument showing PRG (the timbre following
      its program's own setting) — the overwhelmingly common case, so check a few.

Note: the Parameter Guide records that CX-3, PolysixEX, SGX-2 and EP-1 ignore portamento
entirely. A timbre on one of those still *displays* the stored value on both the panel and
in the app, so it is still a valid readout comparison — it just won't be audible.

## 17. Combi layer editing (timbre program & status)
Status: **confirmed** on hardware (2026-07-26 — program replacement, cross-bank repointing,
Off/Int status changes and the MIDI-channel masking check all passed). The status field
(timbre +2 bits 5–7) is now write-verified, and the vendor SysEx dump independently names
the same layout: MIDI channel in bits 4–0, status in bits 7–5 with values Off..External2.
- [x] In a USER combi (Combis tab → Edit → **Timbres**), point one timbre at an obviously
      different program — a pad row switched to an organ, chosen by name — download, load:
      that layer plays the new program and the combi's other layers are unchanged.
- [x] Repoint a timbre to a program in a *different* bank (e.g. from INT-A to USER-C), so
      the stored bank id and the on-screen bank differ: the instrument recalls the program
      you picked, not a same-numbered program from the wrong bank.
- [x] Switch a playing timbre's status to **Off**, download, load: that layer is silent and
      the rest of the combi is untouched.
- [x] Take an unused (Off) timbre, set it to **Int** and point it at a program, download,
      load: the new layer plays — a layer added from nothing.
- [x] On the instrument's Timbre Parameters → MIDI page, the **MIDI channel** of every
      timbre you touched is exactly what it was before (the masking check).

## 18. Song retargeting (companion .SNG)
Status: **confirmed** on hardware (2026-07-26 — songs read correctly off a Save All .SNG, and
both a single program move and a whole-bank sort carried their tracks, with the set lists and
combis on the same bank still recalling correctly). This is the first write to a *second*
file, and the first time the sound-content mapping was proven against the instrument rather
than against itself: programs are followed by what they sound like, not by the operation that
moved them. It also closes the long-standing "songs aren't retargeted" limitation.
The .SNG container is now write-verified: `BMT1` holds one 7810-byte record per song — a
combi-sized timbre set — whose program reference bytes (+0 number, +1 bank PcgId) are the
same pair §4 rewrites in bulk.
- [x] Save All on the instrument, open the .PCG and then its .SNG in PCGUtil: the **Songs**
      tab lists your songs, and each track shows the program it loads, resolved to a name.
      Compare against the instrument's Sequencer P0 track list — they should agree.
- [x] Move one program that a song track uses (Programs → Edit → ▲/▼), download **both**
      files, load both: the song still plays the same sound on that track.
- [x] **Sort A–Z** a program bank several song tracks use — the harder case, since most of
      the bank moves at once — download both, load both: every track still plays what it did.
- [x] The set lists and combis that used the same bank also still recall correctly (the
      other two reference graphs, unaffected by the song work).
- [x] Undo a program move before downloading: the Songs tab's "tracks followed" banner
      disappears and the .SNG download offer goes away, because the file is unchanged again.

## 19. Program effects into a combi (Copy From Program, offline)
Status: **confirmed 2026-07-27, mechanism corrected 2026-07-31 — one re-check pending.**
The 2026-07-31 effect-parameter probe revealed that each slot's parameters sit 64 bytes
BEFORE its header, so the original copy moved header/parameter pairs that were off by one
slot — the effect types, routing, chains and placements were all right (what the six checks
verified), but a copied effect could carry a neighbor's knob settings, which listening
didn't catch. The copy now moves each slot's header and its true parameter area as a pair
(and the MFX/TFX regions were re-drawn to 912–1051 / 1052–1189, params + headers complete —
including TFX2's, whose "missing" parameters turned out to live at 1120).
- [ ] **Re-check (post-correction):** copy a program with a distinctively *set* effect
      (e.g. an extreme EQ or a very long delay) onto a spare timbre, download, load: the
      copied effect's PARAMETER VALUES on the instrument's editor page match the source
      program's exactly — not just the effect name.
- [x] Pick a combi with free IFX slots and a spare timbre. Point the timbre at a program with
      distinctive effects (the FX button beside the program picker shows the plan: how many
      slots it needs and exactly where they land). Apply, download, load: the layer sounds
      like the program did in Program mode — effects included.
- [x] On the instrument's IFX page for that combi: the copied effects sit in exactly the
      slots the plan predicted, switched on/off as the program had them, and every effect
      that was already in the combi is byte-for-byte where it was.
- [x] The combi's **other timbres sound completely unchanged** (the additive guarantee).
- [x] If the source program chains effects (e.g. compressor → EQ), the chain still runs in
      order at its new slot numbers.
- [x] Copy with the **MFX** box ticked from a program whose master effects differ from the
      combi's: the combi's MFX1/2 now match the program's — and, as warned, every layer
      sounds different through them. Same check for **TFX**, listening specifically for
      whether TFX2's parameters came across (the dump's soft spot).
- [x] Clear an unused insert effect (✕ on the effects list), download, load: the slot is
      empty on the instrument and nothing else changed. The app refuses to clear a slot
      anything plays through — confirm the refusal names the timbre correctly.

## 20. Re-point references (rule-based bulk retarget)
Status: **confirmed** on hardware (2026-07-27 — all five checks passed: the copy-of-itself
re-point recalled identically everywhere, slot settings survived, a range rule landed
offset-correct, a song-hitting rule worked with both files loaded, and the Duplicates
one-click left the twins silent-identical and unreferenced). With this, **every PCG Tools
feature short of multi-model coverage is not just shipped but hardware-verified** — the
rule mapping was the last new logic standing on unit tests alone.
- [x] Re-point one program that combis and a set list use to a **copy of itself** elsewhere
      (make the copy first), download (both files if the .SNG is loaded), load: everything
      that used it plays exactly as before — same sound from the new location.
- [x] A repointed set-list slot keeps its color, volume, transpose, hold time, and notes.
- [x] A **range rule** (e.g. three programs to a new spot): each reference lands
      offset-correct — spot-check two on the instrument's combi timbre pages.
- [x] With the .SNG loaded and a rule that hits a song track: the song plays the new target
      after loading both files.
- [x] **Duplicates one-click**: on a duplicated sound, "use this copy" — recall a combi that
      used one of the twins: identical sound (the copies are byte-identical), and the Usage
      tab now shows the twins at zero references.

## 21. Effect settings readout (read-only decode)
Status: **probe-anchored, panel compare pending.** The decode already carries the strongest
evidence a readout can have: the 2026-07-31 single-parameter probe (Stereo Dyna Compressor,
Wet/Dry 37 / Sensitivity 99 / Attack 61 / Output Level 73 dialed on the instrument's own
panel) decodes bit-exactly, and the corpus scan holds at 0 out-of-range fields in 299,638
across both backups and all sixteen slots. What remains is breadth: confirming a few OTHER
effect types' sheets against their panel pages. Read-only — nothing writes these bytes.
- [ ] Open a busy combi, click two or three effect chips of different types (a delay, a
      reverb, an EQ), and compare each sheet against the instrument's Insert/Master Effect
      editor page for the same combi: the values match, field for field.
- [ ] A program's FX badge → chip → sheet matches the program's own effect pages the same
      way.
- [ ] Wet/Dry reads as the instrument writes it (e.g. "37:63"), and modulation sources show
      by name (Off, JS X, Ribbon…).

## Known limitation
- A song track whose program was **overwritten** (paste/clear, not moved) keeps pointing at
  that slot and will play whatever now lives there. That is deliberate — the sound it used is
  gone from the file — and the Songs tab says so.

## Pass criteria
The edited `.PCG` loads cleanly, and **every patch and reference recalls the same sound as
before** — only the positions you intentionally changed should differ.
