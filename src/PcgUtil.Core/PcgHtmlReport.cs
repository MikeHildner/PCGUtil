using System.Net;
using System.Text;

namespace PcgUtil.Core;

/// <summary>
/// Builds printable, self-contained HTML reports from already-decoded data — a set list
/// (the gig sheet), all set lists, or the program usage report. Output is HTML-escaped with
/// simple print-friendly inline CSS.
/// </summary>
public static class PcgHtmlReport
{
    public static string SetList(SetList setList, PcgCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(setList);
        ArgumentNullException.ThrowIfNull(catalog);
        var body = new StringBuilder();
        AppendSetListSection(body, setList, catalog, pageBreak: false);
        return Page($"Set List {setList.Index:D3} - {setList.DisplayName}", body.ToString());
    }

    public static string AllSetLists(IReadOnlyList<SetList> setLists, PcgCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(setLists);
        ArgumentNullException.ThrowIfNull(catalog);
        var body = new StringBuilder();
        bool first = true;
        foreach (var setList in setLists)
        {
            if (!setList.NamedSlots.Any())
                continue; // skip empty set lists
            AppendSetListSection(body, setList, catalog, pageBreak: !first);
            first = false;
        }
        return Page("Set Lists", body.ToString());
    }

    public static string Usage(UsageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var body = new StringBuilder();
        body.Append("<h1>Program usage</h1>");
        body.Append("<p class=\"sub\">")
            .Append(report.Programs.Count).Append(" programs referenced, ")
            .Append(report.UnreferencedPrograms.Count).Append(" unreferenced programs, ")
            .Append(report.UnreferencedCombis.Count).Append(" unreferenced combis.</p>");
        body.Append("<table><thead><tr><th>Program</th><th>References</th></tr></thead><tbody>");
        foreach (var p in report.Programs)
        {
            body.Append("<tr><td>")
                .Append(Esc($"{PcgBankLabels.Program(p.BankIndex)} #{p.Number:D3} - {p.Name}"))
                .Append("</td><td>").Append(p.ReferenceCount).Append("</td></tr>");
        }
        body.Append("</tbody></table>");
        return Page("Program usage", body.ToString());
    }

    private static void AppendSetListSection(StringBuilder body, SetList setList, PcgCatalog catalog, bool pageBreak)
    {
        var songs = setList.NamedSlots.ToList();
        body.Append(pageBreak ? "<h1 class=\"page-break\">" : "<h1>").Append(Esc(setList.DisplayName)).Append("</h1>");
        body.Append("<p class=\"sub\">Set List ").Append(setList.Index.ToString("D3"))
            .Append(", ").Append(songs.Count).Append(songs.Count == 1 ? " song" : " songs").Append("</p>");
        body.Append("<table><thead><tr><th>Slot</th><th>Song</th><th>Loads</th></tr></thead><tbody>");
        foreach (var slot in songs)
        {
            body.Append("<tr><td>").Append(slot.Index.ToString("D3")).Append("</td><td>")
                .Append(Esc(slot.Name)).Append("</td><td>")
                .Append(Esc(SlotLoads(slot, catalog))).Append("</td></tr>");
        }
        body.Append("</tbody></table>");
    }

    private static string SlotLoads(SetListSlot slot, PcgCatalog catalog)
    {
        if (slot.Reference.Kind == PcgItemKind.Song)
            return "Song";
        string label = slot.Reference.Kind == PcgItemKind.Program
            ? PcgBankLabels.Program(PcgCatalog.ProgramBankIndexForPcgId(slot.Reference.Bank))
            : PcgBankLabels.Combi(slot.Reference.Bank);
        var head = $"{slot.Reference.Kind} {label} #{slot.Reference.Index:D3}";
        var name = catalog.Resolve(slot.Reference);
        return name is null ? head : $"{head} - {name}";
    }

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private static string Page(string title, string body)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>")
          .Append(Esc(title)).Append("</title>\n<style>\n");
        sb.Append("body{font-family:system-ui,Segoe UI,Arial,sans-serif;margin:1.5rem;color:#111;}");
        sb.Append("h1{font-size:1.4rem;margin:1rem 0 .25rem;}");
        sb.Append(".sub{color:#666;font-size:.85rem;margin:0 0 1rem;}");
        sb.Append("table{border-collapse:collapse;width:100%;margin-bottom:1rem;}");
        sb.Append("th,td{border:1px solid #ccc;padding:4px 8px;text-align:left;font-size:.9rem;vertical-align:top;}");
        sb.Append("th{background:#f3f3f3;}");
        sb.Append("@media print{body{margin:0;}.page-break{page-break-before:always;}}");
        sb.Append("\n</style>\n</head>\n<body>\n").Append(body).Append("\n</body>\n</html>\n");
        return sb.ToString();
    }
}
