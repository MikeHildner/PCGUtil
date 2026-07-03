# Hardware testing checklist

PCGUtil's reference retargeting is covered by automated tests, so these checks confirm two
things on the workstation itself:

1. it **accepts** a surgically-edited `.PCG` without error, and
2. its interpretation of the references **matches ours** — every patch still recalls the same
   sound after an edit.

This file is the **internal tracker**: per-section Status lines record what has been verified
on our own hardware, and items may reference patches from our sample file. The public,
file-agnostic variant every visitor sees is `src/PcgUtil.Web/wwwroot/hardware-testing.html`
(linked from the app sidebar as "Hardware tests") — same checks in substance, no statuses,
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
Status: **pending** (highest priority — new dual-retarget write path).
- [ ] In the **Usage** tab (read-only), set **Sort** to *Most used* and **Bank** to a USER bank — the top row is the best candidate. Note its bank + number and one combi from its "show" list.
- [ ] In the **Programs** tab, choose that bank and type the number (or name) in **Find** to jump to the program. Turn on **Edit mode**, clear Find so the neighbor is visible, and click ▼ (or ▲) to swap — references retarget automatically.
- [ ] The programs are in their new positions and each sounds right.
- [ ] Combis that used the moved program still load it in every timbre — open a couple from its "Used by" list; they sound identical to before.
- [ ] A set-list slot that loads the moved program directly still recalls it.
- [ ] Copy a program: the destination sounds like the source. Rename: the name shows.

## 5. Song-slot guard (edge case)
Status: **pending**.
- [ ] The Song-type slot "Sequence" (Set List 15, slot 31) is unchanged after a combi/program reorg — especially one that touches the first INT-A combi/program (bank 0, #0), whose bytes it collides with.

## 6. Sort & compact banks
Status: **pending** (same write path as reorg — one confirm run should cover it).
- [ ] Sort a USER combi bank A–Z (Combis tab → Edit mode → **Sort A–Z**): the device shows the new order, init/empty slots last, and a set-list song that used a sorted combi still recalls the same sound.
- [ ] Sort or compact a USER program bank: a combi that used a moved program still sounds identical, and a program-type set-list slot still recalls its program.
- [ ] **Compact** only moves init/empty slots to the end — every named patch keeps its relative order.

## Known limitation
- Sequencer **songs** that reference a moved program are **not** retargeted (set-list and combi
  references are). If you use songs, spot-check a song's tracks after a program reorg.

## Pass criteria
The edited `.PCG` loads cleanly, and **every patch and reference recalls the same sound as
before** — only the positions you intentionally changed should differ.
