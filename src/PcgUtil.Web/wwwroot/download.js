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
