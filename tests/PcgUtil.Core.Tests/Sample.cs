using PcgUtil.Core;

namespace PcgUtil.Core.Tests;

/// <summary>Locates and loads the sample PCG for tests: the first *.PCG (by name) in the
/// repo's untracked <c>files/</c> directory. Content assertions are pinned to whatever file
/// is there, so swapping in a new sample means re-pinning the expected values.</summary>
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
