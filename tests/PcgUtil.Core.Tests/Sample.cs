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
