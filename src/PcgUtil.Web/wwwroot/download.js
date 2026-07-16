// Theme handling. The saved preference is "light" | "dark"; absent = follow the OS.
// The layout renders statically, so the sidebar toggle drives this with plain JS.
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
