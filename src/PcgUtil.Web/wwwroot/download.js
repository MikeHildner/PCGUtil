// Theme handling. The saved preference is "light" | "dark"; absent = follow the OS.
// The layout renders statically, so the top-bar toggle drives this with plain JS.
window.pcgApplyTheme = () => {
    let pref = null;
    try { pref = localStorage.getItem("pcgTheme"); } catch (e) { }
    const dark = pref ? pref === "dark"
        : window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
    document.documentElement.setAttribute("data-bs-theme", dark ? "dark" : "light");
    const label = document.getElementById("theme-toggle-label");
    if (label)
        label.textContent = "Theme: " + (pref ?? "auto");
};

window.pcgCycleTheme = () => {
    let pref = null;
    try { pref = localStorage.getItem("pcgTheme"); } catch (e) { }
    const next = pref === null ? "light" : pref === "light" ? "dark" : null;
    try {
        if (next === null) localStorage.removeItem("pcgTheme");
        else localStorage.setItem("pcgTheme", next);
    } catch (e) { }
    window.pcgApplyTheme();
};

// Follow OS changes while in auto mode, and set the toggle label on load.
if (window.matchMedia)
    window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => window.pcgApplyTheme());
document.addEventListener("DOMContentLoaded", () => window.pcgApplyTheme());

// Triggers a browser download of in-memory text produced by the server (CSV / JSON export).
window.pcgDownload = (filename, mime, text) => {
    const blob = new Blob([text], { type: mime });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// Saves binary bytes (streamed from .NET) as a file download.
window.pcgSaveFile = async (filename, streamRef) => {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// Stable per-browser id for the server-side session-restore cache. Mirrors the pcgTheme
// try/catch pattern: storage-blocked browsers return null and simply run without restore.
window.pcgBrowserId = () => {
    try {
        let id = localStorage.getItem("pcgBrowserId");
        if (!id) {
            id = (crypto.randomUUID && crypto.randomUUID())
                || Date.now().toString(36) + "-" + Math.random().toString(36).slice(2);
            localStorage.setItem("pcgBrowserId", id);
        }
        return id;
    } catch (e) {
        return null;
    }
};

// "/" focuses the always-on header search; Escape blurs it. Pure JS so the hotkey costs
// no circuit round trip and works from any tab.
document.addEventListener("DOMContentLoaded", () => {
    document.addEventListener("keydown", (e) => {
        const quick = document.getElementById("pcg-quick-search");
        if (!quick) return; // no file loaded — no header, nothing to focus
        if (e.key === "Escape") {
            if (e.target === quick) quick.blur(); // Home's keydown handler clears the query
            return;
        }
        if (e.key !== "/" || e.ctrlKey || e.metaKey || e.altKey) return;
        const t = e.target;
        if (t instanceof Element && (t.tagName === "INPUT" || t.tagName === "TEXTAREA"
            || t.tagName === "SELECT" || t.isContentEditable))
            return; // "/" typed into Notes / search / filter fields stays a "/"
        e.preventDefault(); // also suppresses Firefox Quick Find
        quick.focus();
    });
});

// Ctrl+Z / Ctrl+Y (Cmd on Mac; Ctrl+Shift+Z = redo) drive the header undo/redo buttons.
// Clicking the real buttons keeps Blazor's delegated handlers, disabled states, and
// no-file guards in charge — no interop objects needed. Inside text fields the browser's
// native text undo stays untouched (same guard as "/").
document.addEventListener("keydown", (e) => {
    if (!(e.ctrlKey || e.metaKey) || e.altKey) return;
    const k = e.key.toLowerCase();
    if (k !== "z" && k !== "y") return;
    const t = e.target;
    if (t instanceof Element && (t.tagName === "INPUT" || t.tagName === "TEXTAREA"
        || t.tagName === "SELECT" || t.isContentEditable))
        return; // typing a rename or note: leave the field's own undo alone
    const btn = document.getElementById(
        (k === "y" || e.shiftKey) ? "pcg-redo-btn" : "pcg-undo-btn");
    if (!btn) return; // no file loaded — no header, nothing to undo
    e.preventDefault();
    if (!btn.disabled) btn.click();
});

// Last chosen Browse|Edit mode ("1" = edit). Absent = Browse, so fresh browsers stay
// stage-safe. Mirrors the pcgTheme try/catch pattern.
window.pcgGetEditMode = () => {
    try { return localStorage.getItem("pcgEditMode") === "1"; } catch (e) { return false; }
};
window.pcgSetEditMode = (on) => {
    try { localStorage.setItem("pcgEditMode", on ? "1" : "0"); } catch (e) { }
};

// ---- Row drag-to-position (edit mode) ----
// The whole gesture runs client-side: Blazor Server can't afford a SignalR round trip
// per dragover, so .NET hears about a drag exactly once, on drop.
let pcgDragNet = null;   // DotNetObjectReference from Home (recreated per circuit)
let pcgDrag = null;      // { table, from } while a grip-drag is live
let pcgDropRow = null;   // row currently showing the insertion indicator

window.pcgInitRowDrag = (dotnetRef) => { pcgDragNet = dotnetRef; };

const pcgDragRowOf = (el) =>
    el instanceof Element ? el.closest("tr[data-drag-index]") : null;
const pcgClearDropMark = () => {
    if (pcgDropRow) pcgDropRow.classList.remove("pcg-drop-above", "pcg-drop-below");
    pcgDropRow = null;
};

document.addEventListener("dragstart", (e) => {
    const handle = e.target instanceof Element ? e.target.closest(".pcg-drag-handle") : null;
    const row = pcgDragRowOf(handle);
    const table = row ? row.closest("[data-drag-table]") : null;
    if (!handle || handle.disabled || !row || !table) return;
    pcgDrag = { table: table.getAttribute("data-drag-table"), from: +row.dataset.dragIndex };
    e.dataTransfer.setData("text/plain", String(pcgDrag.from)); // Firefox refuses dataless drags
    e.dataTransfer.effectAllowed = "move";
    e.dataTransfer.setDragImage(row, 24, row.offsetHeight / 2); // ghost the row, not the grip
    row.classList.add("pcg-drag-source");
});

document.addEventListener("dragover", (e) => {
    if (!pcgDrag) return;
    const row = pcgDragRowOf(e.target);
    const table = row ? row.closest("[data-drag-table]") : null;
    if (!row || !table || table.getAttribute("data-drag-table") !== pcgDrag.table) return;
    e.preventDefault();                      // this is what permits the drop
    e.dataTransfer.dropEffect = "move";
    const to = +row.dataset.dragIndex;
    if (row !== pcgDropRow) pcgClearDropMark();
    if (to === pcgDrag.from) return;
    pcgDropRow = row;
    const cls = to > pcgDrag.from ? "pcg-drop-below" : "pcg-drop-above";
    if (!row.classList.contains(cls)) {      // touch the DOM only when the edge flips
        row.classList.remove("pcg-drop-above", "pcg-drop-below");
        row.classList.add(cls);
    }
});

document.addEventListener("dragleave", (e) => {
    if (pcgDrag && e.relatedTarget === null) pcgClearDropMark(); // left the window
});

document.addEventListener("drop", (e) => {
    if (!pcgDrag) return;
    const drag = pcgDrag;
    pcgDrag = null;
    pcgClearDropMark();
    const row = pcgDragRowOf(e.target);
    const table = row ? row.closest("[data-drag-table]") : null;
    if (!row || !table || table.getAttribute("data-drag-table") !== drag.table) return;
    e.preventDefault();
    const to = +row.dataset.dragIndex;
    if (to === drag.from || !pcgDragNet) return;
    pcgDragNet.invokeMethodAsync("OnRowDrop", drag.table, drag.from, to)
        .catch(() => { });                   // circuit gone: nothing to do client-side
});

document.addEventListener("dragend", () => { // fires after drop AND on Esc / out-of-window cancel
    pcgDrag = null;
    pcgClearDropMark();
    document.querySelectorAll(".pcg-drag-source")
        .forEach(r => r.classList.remove("pcg-drag-source"));
});

// ---- Merge drag (Merge view): source-pane row → target-pane slot ----
// A second gesture namespace; the reorder gesture above stays same-table only. The
// same DotNetObjectReference serves both — .NET hears one OnMergeDrop per gesture.
let pcgMerge = null;     // { kind, slot, bank, index } while a merge drag is live
let pcgMergeRow = null;  // target row currently highlighted

const pcgMergeTargetOf = (el) =>
    el instanceof Element ? el.closest("tr[data-merge-target]") : null;
const pcgClearMergeMark = () => {
    if (pcgMergeRow) pcgMergeRow.classList.remove("pcg-merge-over");
    pcgMergeRow = null;
};

document.addEventListener("dragstart", (e) => {
    const row = e.target instanceof Element ? e.target.closest("tr.pcg-merge-row") : null;
    if (!row) return;
    pcgMerge = {
        kind: row.dataset.mergeKind, slot: +row.dataset.mergeSlot,
        bank: +row.dataset.mergeBank, index: +row.dataset.mergeSrcIndex,
    };
    e.dataTransfer.setData("text/plain", row.dataset.mergeSrcIndex); // Firefox needs data
    e.dataTransfer.effectAllowed = "copy";
    row.classList.add("pcg-drag-source");
});

document.addEventListener("dragover", (e) => {
    if (!pcgMerge) return;
    const row = pcgMergeTargetOf(e.target);
    if (!row || !(row.dataset.mergeAccepts || "").split(",").includes(pcgMerge.kind)) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "copy";
    if (row !== pcgMergeRow) {
        pcgClearMergeMark();
        pcgMergeRow = row;
        row.classList.add("pcg-merge-over");
    }
});

document.addEventListener("drop", (e) => {
    if (!pcgMerge) return;
    const drag = pcgMerge;
    pcgMerge = null;
    pcgClearMergeMark();
    const row = pcgMergeTargetOf(e.target);
    if (!row || !(row.dataset.mergeAccepts || "").split(",").includes(drag.kind)) return;
    e.preventDefault();
    if (!pcgDragNet) return;
    pcgDragNet.invokeMethodAsync("OnMergeDrop", drag.kind, drag.slot, drag.bank, drag.index,
        +row.dataset.mergeBank, +row.dataset.mergeIndex).catch(() => { });
});

document.addEventListener("dragend", () => {
    pcgMerge = null;
    pcgClearMergeMark();
    document.querySelectorAll("tr.pcg-merge-row.pcg-drag-source")
        .forEach(r => r.classList.remove("pcg-drag-source"));
});

// One-shot viewport clamp for the fixed-position row menu: after Blazor renders it at
// the cursor, nudge it fully on-screen. Stateless, mirrors pcgRevealRow.
window.pcgClampMenu = (id) => {
    const el = document.getElementById(id);
    if (!el) return;
    const r = el.getBoundingClientRect(), pad = 8;
    let left = r.left, top = r.top;
    if (r.right > window.innerWidth - pad) left = window.innerWidth - pad - r.width;
    if (r.bottom > window.innerHeight - pad) top = window.innerHeight - pad - r.height;
    left = Math.max(pad, left);
    top = Math.max(pad, top);
    if (left !== r.left) el.style.left = left + "px";
    if (top !== r.top) el.style.top = top + "px";
};

// Scrolls a just-jumped-to table row into view and flashes it so the eye lands there.
// block:center keeps the row clear of the sticky file header.
window.pcgRevealRow = (id) => {
    const el = document.getElementById(id);
    if (!el) return;
    const reduce = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    el.scrollIntoView({ block: "center", behavior: reduce ? "auto" : "smooth" });
    el.classList.remove("row-reveal");
    void el.offsetWidth; // restart the animation when revealing the same row twice
    el.classList.add("row-reveal");
    setTimeout(() => el.classList.remove("row-reveal"), 2200);
};
