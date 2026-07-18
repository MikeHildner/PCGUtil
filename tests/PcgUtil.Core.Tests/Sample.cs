using PcgUtil.Core;

namespace PcgUtil.Core.Tests;

/// <summary>Locates and loads the sample PCG for tests: the first *.PCG (by name) in the
/// repo's untracked <c>files/</c> directory, skipping derived artifacts (names containing
/// "edited" or "test") so app-produced downloads sitting next to the pristine export don't
/// change what the suite pins against. Content assertions are pinned to whatever file is
/// there, so swapping in a new sample means re-pinning the expected values.</summary>
internal static class Sample
{
    public static string Path { get; } = Find();

    public static byte[] Bytes() => File.ReadAllBytes(Path);

    public static PcgFile Parse() => PcgReader.Parse(Bytes());

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var filesDir = System.IO.Path.Combine(dir.FullName, "files");
            if (Directory.Exists(filesDir))
            {
                var candidate = Directory.EnumerateFiles(filesDir, "*.PCG")
                    .Where(p =>
                    {
                        var name = System.IO.Path.GetFileName(p);
                        return !name.Contains("edited", StringComparison.OrdinalIgnoreCase)
                            && !name.StartsWith("test", StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (candidate is not null)
                    return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"No sample *.PCG found in a files/ directory walking up from {AppContext.BaseDirectory}");
    }
}

/// <summary>
/// Locates the optional slot-color probe PCG (set list 016 has slots 0–15 set to the 16
/// picker colors in order, slot 0 volume 100, slots 1/2 transpose +2/−1 — captured on
/// hardware 2026-07-18). Tests that depend on it silently pass when it's absent.
/// </summary>
internal static class ColorsProbe
{
    public static PcgFile? Parse()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var filesDir = System.IO.Path.Combine(dir.FullName, "files");
            if (Directory.Exists(filesDir))
            {
                var path = Directory.EnumerateFiles(filesDir, "colors-probe.PCG").FirstOrDefault();
                return path is null ? null : PcgReader.Parse(File.ReadAllBytes(path));
            }
            dir = dir.Parent;
        }
        return null;
    }
}

/// <summary>
/// Locates the optional favorites probe (a hardware export where combi USER-A 096 "JUMP"
/// and program USER-GG 000 "GET LUCKY VOCODER" were starred on the instrument — the diff
/// that located the program favorite bit, 2026-07-18). Silently absent-safe.
/// </summary>
internal static class FavoritesProbe
{
    public static PcgFile? Parse()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var filesDir = System.IO.Path.Combine(dir.FullName, "files");
            if (Directory.Exists(filesDir))
            {
                var path = Directory.EnumerateFiles(filesDir, "20260718c.PCG").FirstOrDefault();
                return path is null ? null : PcgReader.Parse(File.ReadAllBytes(path));
            }
            dir = dir.Parent;
        }
        return null;
    }
}

/// <summary>
/// Locates the optional vendor-pack PCG (a partial file carrying only a few USER banks) under
/// files/. Tests that depend on it must silently pass when it's absent — unlike the main
/// sample, it isn't required.
/// </summary>
internal static class VendorPack
{
    public static string? Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var filesDir = System.IO.Path.Combine(dir.FullName, "files");
            if (Directory.Exists(filesDir))
                return Directory.EnumerateFiles(filesDir, "AUDORA*.PCG", SearchOption.AllDirectories)
                    .FirstOrDefault();
            dir = dir.Parent;
        }
        return null;
    }

    public static PcgFile? Parse() =>
        Find() is { } path ? PcgReader.Parse(File.ReadAllBytes(path)) : null;
}
